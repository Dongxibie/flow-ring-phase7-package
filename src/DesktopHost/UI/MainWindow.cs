using System.Globalization;
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
/// v18.4 终极诊断版：
/// - 全局异常 handler（AppDomain + TaskScheduler）
/// - 同步访问 CoreWebView2（STA 线程安全）
/// - Task.Run 异步 dump + ConfigureAwait(false)（ThreadPool）
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

    private static void WriteExceptionToFile(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ').Append(source).Append('\n');
            sb.Append("Exception: ").Append(ex.GetType().FullName).Append('\n');
            sb.Append("Message: ").Append(ex.Message).Append('\n');
            sb.Append("StackTrace: ").Append(ex.StackTrace).Append('\n');
            if (ex.InnerException != null)
            {
                sb.Append("InnerException: ").Append(ex.InnerException.GetType().FullName).Append('\n');
                sb.Append("InnerMessage: ").Append(ex.InnerException.Message).Append('\n');
                sb.Append("InnerStackTrace: ").Append(ex.InnerException.StackTrace).Append('\n');
            }
            File.AppendAllText(ExceptionLogPath, sb.ToString());
        }
        catch
        {
            // 不抛异常
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

    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;

        _webView.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            try
            {
                _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                    e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);
            }
            catch (Exception ex)
            {
                WriteExceptionToFile("NavigationCompleted.Sync", ex);
            }

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