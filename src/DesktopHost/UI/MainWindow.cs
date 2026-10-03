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
/// v10 大修复（一次性）：
/// 1. WebView2 Navigate URL 改回 https://flowring.local/index.html（v9 改 '/' 失败 → ERR_ACCESS_DENIED）
///    SDK 1.0.2651.64 SetVirtualHostNameToFolderMapping 没 defaultDocument 重载（reflection 验证），
///    WebView2 不会自动 fallback 到 index.html
/// 2. main.tsx 改 path '*' catch-all（v10 commit 268de9cb）+ Layout element + children 相对路径
///    让任何路径（包括 /index.html、/studio 等）都进 Layout
/// 3. NavigationCompleted 后注入 JS 拿 {url, title, bodyText.slice, hasLayout, hasHeader} 把结果 log
///
/// v9 → v10 调整：
/// - v9 Navigate https://flowring.local/ → ERR_ACCESS_DENIED（实测）
/// - v10 Navigate https://flowring.local/index.html + main.tsx catch-all → 任意路径进 Layout
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

        // v10：Navigate 到 https://flowring.local/index.html（确保 HTML 加载成功）
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
    /// v10 诊断：NavigationCompleted 后注入 JS 拿页面真实状态（url/title/bodyText/hasLayout/hasHeader/rootChildren）。
    /// </summary>
    private void AttachNavigationListener()
    {
        if (_webView.CoreWebView2 is null) return;
        _webView.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            _logger.LogInformation("MainWindow NavigationCompleted：status={Status}, httpStatusCode={HttpStatusCode}, url={Uri}",
                e.WebErrorStatus, e.HttpStatusCode, _webView.CoreWebView2.Source);
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