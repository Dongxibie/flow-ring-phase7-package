using System.IO;
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
/// v18.3 修法（大审查后定位真实根因）：
///
/// v18.2 暴露的错误：System.InvalidOperationException: CoreWebView2 can only be accessed from the UI thread
///
/// 这意味着 WebView2 的 CoreWebView2 属性访问必须在创建它的 STA UI 线程。
/// v18.2 错误地把 await 加 ConfigureAwait(false) → await 跑到 ThreadPool → 访问 CoreWebView2 抛错。
///
/// 之前 v10-v18.1 NavigationCompleted async lambda 都有"实际死锁"——
///   * 默认 ConfigureAwait(true) 让 await post 回 STA
///   * STA 被 Application.Run() 阻塞
///   * await 永远不返回 → handler 抛死锁 → async void 默默吞异常
/// 这就是为什么之前 log 只看到第一行（status=Unknown）但看不到任何后续 dump。
///
/// v18.3 修：handler 同步（synchronous），立即调 Task.Run 把异步操作跑到 ThreadPool
///   * handler 第一行（log 信息）立即输出（STA 线程）
///   * 后续 CoreWebView2.ExecuteScriptAsync 调用 在 ThreadPool（避免 STA 死锁 + 错误）
///   * ConfigureAwait(false) 让 await 不 post 回 STA
///   * 用 .ConfigureAwait(false) 而不是默认，避免 STA 上下文捕获
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
                _logger.LogInformation("CoreWebView2 初始化完成");
                AttachNavigationListener();
                if (!string.IsNullOrEmpty(_pendingFrontendDist) && !string.IsNullOrEmpty(_pendingNavigationUri))
                {
                    try
                    {
                        _webView.CoreWebView2?.SetVirtualHostNameToFolderMapping(
                            VirtualHost,
                            _pendingFrontendDist,
                            CoreWebView2HostResourceAccessKind.Allow);
                        _webView.CoreWebView2?.Navigate(_pendingNavigationUri);
                        _logger.LogInformation("MainWindow 异步路径已 SetVirtualHost + Navigate：{Uri}", _pendingNavigationUri);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "异步路径 SetVirtualHost + Navigate 失败");
                    }
                }
            }
            else
            {
                _isReady = false;
                _logger.LogError(e.InitializationException, "CoreWeb2 初始化失败");
                if (!Visible) Show();
            }
        };

        Load += (_, _) =>
        {
            _logger.LogInformation("MainWindow Load 完成，host 启动链路全部就绪");
        };
    }

    public async Task InitializeAsync(string frontendDistPath, CancellationToken ct)
    {
        try
        {
            // 默认 ConfigureAwait(true)：回到创建 CoreWebView2 的 STA 线程
            // 避免 v18.2 的 InvalidOperationException
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
                _logger.LogInformation("MainWindow 已 SetVirtualHost + Navigate（同步路径）：{Uri}", IndexUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步路径 SetVirtualHost + Navigate 失败");
            }
        }
        else
        {
            _logger.LogInformation("MainWindow 已存 pending URI，等待 CoreWebView2 异步初始化：{Uri}", IndexUrl);
        }
    }

    /// <summary>
    /// v18.3 关键修复：handler 同步（synchronous），立即 fire-and-forget Task.Run。
    ///
    /// 之前 v10-v18.2 async lambda 死锁根因：
    /// - 默认 ConfigureAwait(true) post 回 STA SynchronizationContext
    /// - STA 被 Application.Run() 阻塞 → 死锁 → async void 默默吞
    ///
    /// v18.3 修法：
    /// - handler 同步：CoreWebView2 属性访问在 STA 线程（安全）
    /// - 第一行 log 立即输出
    /// - 后续 ExecuteScriptAsync 调用 fire-and-forget Task.Run 到 ThreadPool
    /// - Task.Run 内 async Task + ConfigureAwait(false) 避免 STA 死锁
    /// </summary>
    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;
        // 同步 handler：在 STA 线程上访问 CoreWebView2 属性
        _webView.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);

            // fire-and-forget：把 async 操作搬到 ThreadPool
            _ = HandleNavigationCompletedAsync();
        };
        _logger.LogInformation("MainWindow NavigationCompleted 监听已注册");
    }

    /// <summary>
    /// v18.3：在 ThreadPool 上跑异步 dump，避免 STA 死锁。
    /// </summary>
    private async Task HandleNavigationCompletedAsync()
    {
        try
        {
            // v18.3：所有 await 都 ConfigureAwait(false)，避免 post 回 STA 死锁
            await DumpPageStateAsync("t+0s").ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
            await DumpPageStateAsync("t+1s").ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);
            await DumpPageStateAsync("t+3s").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow HandleNavigationCompletedAsync 失败");
        }
    }

    private async Task DumpPageStateAsync(string tag)
    {
        try
        {
            // v18.3：await ConfigureAwait(false) 避免 STA 死锁
            var json = await _webView.CoreWebView2!.ExecuteScriptAsync(
                @"(() => {
                    const errs = window.__errors || [];
                    const root = document.getElementById('root');
                    return JSON.stringify({
                        url: location.href,
                        title: document.title,
                        bodyText: document.body ? document.body.innerText.slice(0, 300) : null,
                        bodyHTMLLen: document.body ? document.body.innerHTML.length : 0,
                        hasHeader: !!document.querySelector('header'),
                        rootExists: !!root,
                        rootInnerLen: root ? root.innerHTML.length : 0,
                        rootChildren: root ? root.children.length : 0,
                        errors: errs,
                        scriptLoadErrors: window.__scriptErrors || []
                    });
                })()").ConfigureAwait(false);
            _logger.LogInformation("MainWindow 页面状态 [{Tag}]：{Json}", tag, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow 注入诊断 JS [{Tag}] 失败", tag);
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

        try
        {
            _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.location.hash = '#/{route}'");
            _logger.LogInformation("跳转路由：{Route}", route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToRoute 失败：{Route}", route);
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