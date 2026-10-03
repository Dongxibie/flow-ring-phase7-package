using System.IO;
using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// 主窗口：WebView2 的唯一宿主，承载前端 SPA。
/// 关闭按钮只 Hide（不退出 host），退出走托盘菜单。
///
/// v12 修法（前端 + host 双轨）：
/// 1. main.tsx 回退 v9 风格 path '/' 父路由 + children index（v12 commit 1c9a812b）
///    v11 嵌套 catch-all 实测破坏 Layout 渲染
/// 2. host 端：NavigationCompleted 后 ExecuteScriptAsync 把 /index.html 改 /
///    React Router 看到路径 '/' → 匹配 path '/' 父 → Layout 渲染 + children index ProfileManagerPage
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
    /// v12 关键修复：NavigationCompleted 后：
    /// 1. 用 history.replaceState 把 URL 改成 '/'（让 React Router 拿到正确路径）
    /// 2. 触发 popstate 事件让 Router 重新解析
    /// 3. 注入诊断 JS 拿页面状态
    /// </summary>
    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;
        _webView.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);

            // v12 关键：替换 URL 为 '/' 让 React Router 匹配 path '/' 父路由
            try
            {
                var currentUrl = _webView.CoreWebView2.Source;
                if (currentUrl.Contains("/index.html"))
                {
                    var replaceJs = "history.replaceState({}, '', '/'); window.dispatchEvent(new PopStateEvent('popstate'));";
                    await _webView.CoreWebView2.ExecuteScriptAsync(replaceJs);
                    _logger.LogInformation("MainWindow URL 已替换：/index.html → /");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MainWindow URL 替换失败");
            }

            // 诊断：拿页面真实状态
            try
            {
                var js = @"JSON.stringify({
                    url: location.href,
                    title: document.title,
                    bodyText: document.body ? document.body.innerText.slice(0, 300) : null,
                    bodyHTMLLen: document.body ? document.body.innerHTML.length : 0,
                    hasHeader: !!document.querySelector('header'),
                    hasNavLink: !!document.querySelector('header a'),
                    rootChildren: document.getElementById('root') ? document.getElementById('root').children.length : 0
                })";
                var json = await _webView.CoreWebView2.ExecuteScriptAsync(js);
                _logger.LogInformation("MainWindow 页面状态：{Json}", json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MainWindow 注入诊断 JS 失败");
            }
        };
        _logger.LogInformation("MainWindow NavigationCompleted 监听已注册");
    }

    /// <summary>
    /// 跳转到前端路由（不重新加载整个页面，走 SPA 内部 hash 切换）。
    /// </summary>
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