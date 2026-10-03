using System.IO;
using System.Text;
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
/// v18.4 终极诊断版（大审查后定位真实根因）：
///
/// v18.3 跑后症状：
/// - handler 第一行 log 输出（"NavigationCompleted：status=Unknown..."）
/// - 但后续 dump log 没有
/// - 第一行后 handler 第一行 log 后停住
///
/// v18.3 的修法（handler 同步 + Task.Run + ConfigureAwait(false)）暴露了更深的问题：
/// - v18.2 ConfigureAwait(false) → CoreWebView2 在 ThreadPool 抛 InvalidOperationException
/// - v18.3 handler 同步 + Task.Run 后 dump 没出现 = Task.Run 任务崩了但 fire-and-forget 吞了
///
/// v18.4 改法：
/// 1. 全局异常捕获
///     - AppDomain.CurrentDomain.UnhandledException → 同步未捕获异常
///     - TaskScheduler.UnobservedTaskException → async Task 未观察异常
///     - 写到 C:\FlowRing-Fix\flowring-exceptions.log（不走 ILogger，可能死锁）
/// 2. 把所有 sync handler 第一行 log 立即在 STA 输出（保证至少有 log）
/// 3. 简化 dump：用 sync 路径调 GetBrowserVersionString（同步 API），拿真实状态
/// 4. 不依赖任何 async lambda ——避免 STA 死锁
///
/// 如果 v18.4 仍只看到第一行 log 而异常文件为空 → 说明异常在更底层（如 WinForms COM 初始化失败）
/// </summary>
public sealed class MainWindow : Form
{
    private const string VirtualHost = "flowring.local";
    private const string IndexUrl = $"https://{VirtualHost}/index.html";
    private const string ExceptionLogPath = @"C:\FlowRing-Fix\flowring-exceptions.log";

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

        // v18.4：全局异常 handler，写到文件（不走 ILogger，避免死锁）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteExceptionToFile("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteExceptionToFile("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

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
    /// 同步方法，写异常到文件（避免 ILogger 死锁）。
    /// </summary>
    private static void WriteExceptionToFile(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:HH:mm:ss.fff} {source} ===");
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"InnerException: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"InnerMessage: {ex.InnerException.Message}");
                sb.AppendLine($"InnerStackTrace: {ex.InnerException.StackTrace}");
            }
            File.AppendAllText(ExceptionLogPath, sb.ToString());
        }
        catch
        {
            // 不再 throw —— 否则可能引发更多异常
        }
    }

    public async Task InitializeAsync(string frontendDistPath, CancellationToken ct)
    {
        try
        {
            await _webView.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex)
        {
            WriteExceptionToFile("InitializeAsync.EnsureCoreWebView2Async", ex);
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
                WriteExceptionToFile("InitializeAsync.SetVirtualHostOrNavigate", ex);
                _logger.LogError(ex, "同步路径 SetVirtualHost + Navigate 失败");
            }
        }
        else
        {
            _logger.LogInformation("MainWindow 已存 pending URI，等待 CoreWebView2 异步初始化：{Uri}", IndexUrl);
        }
    }

    /// <summary>
    /// v18.4 同步 handler + 同步 thread 同步函数调 dump（避免 STA 死锁）。
    /// 同步 handler 在 STA 线程触发，CoreWebView2 访问安全。
    /// 同步 API：
    /// - _webView.CoreWebView2.Source（同步）
    /// - GetBrowserVersionString（同步）
    /// 异步 API（fire-and-forget 到 ThreadPool）：
    /// - ExecuteScriptAsync
    /// </summary>
    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;

        _webView.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            try
            {
                // 同步部分：STA 线程安全
                _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                    e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);
                _logger.LogInformation("MainWindow GetBrowserVersionString：{Version}", _webView.CoreWebView2.BrowserVersionString);
            }
            catch (Exception ex)
            {
                WriteExceptionToFile("NavigationCompleted.Sync", ex);
            }

            // 异步部分：fire-and-forget 到 ThreadPool
            _ = Task.Run(async () =>
            {
                try
                {
                    await DumpPageStateAsync("t+0s");
                    await Task.Delay(1000);
                    await DumpPageStateAsync("t+1s");
                    await Task.Delay(2000);
                    await DumpPageStateAsync("t+3s");
                }
                catch (Exception ex)
                {
                    WriteExceptionToFile("NavigationCompleted.AsyncTaskRun", ex);
                }
            });
        };
        _logger.LogInformation("MainWindow NavigationCompleted 监听已注册");
    }

    private async Task DumpPageStateAsync(string tag)
    {
        try
        {
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
                })()");
            _logger.LogInformation("MainWindow 页面状态 [{Tag}]：{Json}", tag, json);
        }
        catch (Exception ex)
        {
            WriteExceptionToFile($"DumpPageStateAsync[{tag}]", ex);
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
            WriteExceptionToFile("NavigateToRoute", ex);
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