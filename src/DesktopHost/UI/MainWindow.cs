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
/// v18.1 增强诊断（大审查后定位真实根因）：
/// - 之前所有 v10-v18 都基于"React Router 路径匹配"假设，但 v18 vanilla useState 完全不依赖路径匹配仍白屏
/// - 真实根因不在 React Router，在更底层：
///   * React 可能没 mount（页面 mount 失败抛异常被 React 默默吞了）
///   * dist 资源可能没加载（WebView2 加载 HTML 但 JS 资源 404）
///   * 组件可能 import 失败（ProfileManagerPage 用 useBridge 钩子可能 throw）
/// v18.1 加诊断：
///   * 在 index.html 注入 inline script 设 window.__errors = [] 拦截 console.error
///   * NavigationCompleted 后注入 JS 拿页面 state + window.__errors
///   * 1s/3s/5s 轮询触发 dump（capture async errors）
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

        // 关闭按钮拦截：只 Hide，不退出 host
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
    /// 异步初始化 WebView2 + 加载前端 dist。
    /// </summary>
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
    /// v18.1 增强诊断：NavigationCompleted 后立刻 + 1s + 3s 多次 dump 页面 state。
    /// 同时在 index.html 注入 inline script（v18.1 vite plugin postbuild）拦截 window.onerror 和 console.error。
    /// 把错误存到 window.__errors 数组，dump 时一起输出。
    /// </summary>
    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;
        _webView.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);

            // v18.1：立刻 + 1s + 3s 多次捕获（页面可能 async render）
            await DumpPageStateAsync("t+0s");
            await Task.Delay(1000);
            await DumpPageStateAsync("t+1s");
            await Task.Delay(2000);
            await DumpPageStateAsync("t+3s");
        };
        _logger.LogInformation("MainWindow NavigationCompleted 监听已注册");
    }

    private async Task DumpPageStateAsync(string tag)
    {
        try
        {
            var js = @"(() => {
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
            })()";
            var json = await _webView.CoreWebView2!.ExecuteScriptAsync(js);
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