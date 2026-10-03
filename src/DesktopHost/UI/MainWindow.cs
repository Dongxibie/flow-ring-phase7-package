using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// 主窗口：WebView2 的唯一宿主，承载前端 SPA。
///
/// v18.5 终极修法：彻底反转通信方向
///
/// v18.3 / v18.4 暴露的真实根因（异常捕获后看到）：
/// - _webView.CoreWebView2 属性 getter 要求 STA 线程
/// - Task.Run 在 ThreadPool 上访问 CoreWebView2 → InvalidOperationException
/// - host 主动调 ExecuteScriptAsync 在 ThreadPool 也抛错
///
/// v18.5 修法（彻底避免 host 主动访问 CoreWebView2 API）：
/// - host 只注册 CoreWebView2InitializationCompleted / WebMessageReceived event handler（同步，无 await）
/// - 前端主动 postMessage 上报状态
/// - host 端只 listen WebMessageReceived
/// - host 不调 ExecuteScriptAsync / SetVirtualHostNameToFolderMapping / Navigate（这些需要 CoreWebView2）
///
/// v18.5 简化：
/// - WebView2Host 仍负责 SetVirtualHost + Navigate（这是 startup 一次性，host 同步调用，STA 线程安全）
/// - MainWindow 只 listen WebMessageReceived event
/// - 客户端 mount 后 postMessage('PAGE_STATE')
/// </summary>
public sealed class MainWindow : Form
{
    private const string VirtualHost = "flowring.local";
    private const string IndexUrl = $"https://{VirtualHost}/index.html";

    private readonly HostController _controller;
    private readonly ILogger<MainWindow> _logger;
    private readonly WebView2 _webView = new();
    private volatile bool _isReady;
    private string? _pendingNavigationUri;
    private string? _pendingFrontendDist;

    public MainWindow(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MainWindow>();
        _logger.LogInformation(
            "MainWindow 构造（线程 {ThreadId}，apartment {Apt}）",
            Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState());

        Text = "Flow Ring";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        Icon = null;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _logger.LogInformation("主窗口被用户关闭，已隐藏到托盘（host 仍在跑）");
            }
        };

        _webView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                _isReady = true;
                _logger.LogInformation(
                    "CoreWebView2 初始化完成（线程 {ThreadId}，apartment {Apt}）",
                    Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.GetApartmentState());
                AttachDiagnostics();
                AttachWebMessageListener();
                if (!string.IsNullOrEmpty(_pendingFrontendDist) && !string.IsNullOrEmpty(_pendingNavigationUri))
                {
                    try
                    {
                        _webView.CoreWebView2?.SetVirtualHostNameToFolderMapping(
                            VirtualHost,
                            _pendingFrontendDist,
                            CoreWebView2HostResourceAccessKind.Allow);
                        _webView.CoreWebView2?.Navigate(_pendingNavigationUri);
                        _logger.LogInformation("MainWindow 已 SetVirtualHost + Navigate：{Uri}", _pendingNavigationUri);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SetVirtualHost + Navigate 失败");
                    }
                }
            }
            else
            {
                _isReady = false;
                _logger.LogError(e.InitializationException, "CoreWebView2 初始化失败");
                if (!Visible) Show();
            }
        };

        Load += (_, _) =>
        {
            _logger.LogInformation("MainWindow Load 完成，host 启动链路全部就绪");
        };
    }

    /// <summary>
    /// v19 诊断：只采集证据，不做修复性改动。
    /// 1. 打开 DevTools 窗口（前端 console 真实错误）
    /// 2. WebResourceRequested（JS/CSS 资源是否被请求）
    /// 3. Navigation/ContentLoading/DOMContentLoaded/ProcessFailed（加载生命周期）
    /// </summary>
    private void AttachDiagnostics()
    {
        if (_webView.CoreWebView2 is null) return;
        var core = _webView.CoreWebView2;

        try
        {
            core.Settings.AreDevToolsEnabled = true;
            // v19 收尾：自动弹 DevTools 是诊断行为，默认关；需要时设 FLOWRING_DEVTOOLS=1 再启动
            if (Environment.GetEnvironmentVariable("FLOWRING_DEVTOOLS") == "1")
            {
                core.OpenDevToolsWindow();
                _logger.LogInformation("DevTools 窗口已打开（FLOWRING_DEVTOOLS=1）");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开 DevTools 窗口失败");
        }

        try
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
                _logger.LogInformation("WebResource 请求：{Uri}", e.Request.Uri);
            _logger.LogInformation("WebResourceRequested 监听已注册");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "注册 WebResourceRequested 失败");
        }

        core.NavigationStarting += (_, e) =>
            _logger.LogInformation("NavigationStarting：{Uri}", e.Uri);
        core.NavigationCompleted += (_, e) =>
            _logger.LogInformation(
                "NavigationCompleted：IsSuccess={IsSuccess} Http={Http} ErrorStatus={Err}",
                e.IsSuccess, e.HttpStatusCode, e.WebErrorStatus);
        core.ContentLoading += (_, _) => _logger.LogInformation("ContentLoading");
        core.DOMContentLoaded += (_, _) => _logger.LogInformation("DOMContentLoaded");
        core.ProcessFailed += (_, e) =>
            _logger.LogError("ProcessFailed：{Kind}", e.ProcessFailedKind);
        _logger.LogInformation("生命周期监听已注册（Navigation/ContentLoading/DOMContentLoaded/ProcessFailed）");
    }

    /// <summary>
    /// v18.5：只 listen WebMessageReceived —— 前端 postMessage 上报状态。
    /// 不调任何 ExecuteScriptAsync / 不主动访问 CoreWebView2 状态。
    /// </summary>
    private void AttachWebMessageListener()
    {
        if (_webView.CoreWebView2 is null) return;
        _webView.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            try
            {
                // e.TryGetWebMessageAsString() 同步方法，STA 线程安全
                var json = e.TryGetWebMessageAsString();
                if (json is not null)
                {
                    // 验证 JSON + 提取关键字段
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var type = root.TryGetProperty("type", out var t) ? t.GetString() : "(no type)";
                        var state = root.TryGetProperty("state", out var s) ? s.ToString() : "(no state)";
                        _logger.LogInformation("MainWindow WebMessageReceived [Type={Type}] [State={State}]", type, state);
                    }
                    catch (JsonException)
                    {
                        // 不是 JSON，直接 log
                        _logger.LogInformation("MainWindow WebMessageReceived（raw）：{Json}", json);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MainWindow WebMessageReceived 处理失败");
            }
        };
        _logger.LogInformation("MainWindow WebMessageReceived 监听已注册");
    }

    public async Task InitializeAsync(string frontendDistPath, CancellationToken ct)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 EnsureCoreWebView2Async 失败");
            throw;
        }

        var indexPath = Path.Combine(frontendDistPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _logger.LogWarning("前端 dist 缺失：{Path}，请先跑 pnpm --filter web build", indexPath);
            return;
        }

        _pendingNavigationUri = IndexUrl;
        _pendingFrontendDist = frontendDistPath;

        // 注意：SetVirtualHostNameToFolderMapping 和 Navigate 在 host 启动时调一次（STA 线程）
        // 这里运行在调用 InitializeAsync 的线程（默认 ConfigureAwait(true) → 同步调用方线程）
        if (_webView.CoreWebView2 is not null)
        {
            try
            {
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost,
                    frontendDistPath,
                    CoreWebView2HostResourceAccessKind.Allow);
                _webView.CoreWebView2.Navigate(IndexUrl);
                _isReady = true;
                _logger.LogInformation("MainWindow 已 SetVirtualHost + Navigate：{Uri}", IndexUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步路径 SetVirtualHost + Navigate 失败");
            }
        }
    }

    public void NavigateToRoute(string route)
    {
        if (!Visible) Show();
        BringToFront();
        Activate();

        if (!_isReady || _webView.CoreWebView2 is null)
        {
            _logger.LogWarning("WebView2 未就绪，已开窗口但路由不跳转：{Route}", route);
            return;
        }

        // v18.5：改成前端 postMessage 跳转（host 不主动 ExecuteScriptAsync）
        try
        {
            _webView.CoreWebView2.PostWebMessageAsString(
                JsonSerializer.Serialize(new { type = "NAVIGATE", route }));
            _logger.LogInformation("已发 NAVIGATE 消息给前端：{Route}", route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostWebMessageAsString 失败：{Route}", route);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _webView.Dispose();
        }
        base.Dispose(disposing);
    }
}