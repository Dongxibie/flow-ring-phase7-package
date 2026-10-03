using System.IO;
using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// 主窗口：WebView2 的唯一宿主，承载前端 SPA。
/// 关闭按钮只 Hide（不退出 host），退出走托盘菜单。
/// v6 修复：CoreWebView2InitializationCompleted 异步路径回调里也 Navigate；
/// v5 实现只在同步路径（_webView.CoreWebView2 is not null）调 Navigate，
/// 异步路径下 CoreWebView2 已赋值但回调没触发 Navigate → 主窗口白屏。
/// </summary>
public sealed class MainWindow : Form
{
    private readonly HostController _controller;
    private readonly ILogger<MainWindow> _logger;
    private readonly WebView2 _webView = new();
    private volatile bool _isReady;
    private string? _pendingNavigationUri;

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

        // v6 关键修复：CoreWebView2InitializationCompleted 异步路径回调里也 Navigate。
        // 同步路径下 InitializeAsync 内 await EnsureCoreWebView2Async 已完成，CoreWebView2 已赋值，
        // Navigate 在 InitializeAsync 内同步调。但异步路径下 EnsureCoreWebView2Async 在回调里
        // 完成，CoreWebView2 在回调触发时才赋值，必须在回调里 Navigate，否则主窗口白屏。
        _webView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
            {
                _isReady = true;
                _logger.LogInformation("CoreWebView2 初始化完成");
                if (!string.IsNullOrEmpty(_pendingNavigationUri))
                {
                    _webView.CoreWebView2?.Navigate(_pendingNavigationUri);
                    _logger.LogInformation("MainWindow 异步路径已 Navigate：{Uri}", _pendingNavigationUri);
                }
            }
            else
            {
                _isReady = false;
                _logger.LogError(e.InitializationException, "CoreWebView2 初始化失败");
                // 即使失败也 Show 窗口，让用户看到错误状态
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
    /// 调用时机：WebView2Host.InitializeAsync 内，Environment.CreateAsync 之后。
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
            // 不抛异常，让主窗口能打开但页面空白
            return;
        }

        var fileUri = new Uri(indexPath).AbsoluteUri;
        _pendingNavigationUri = fileUri;

        // 同步路径：CoreWebView2 在 await 完成后已赋值（WinForms WebView2 在已创建 Environment 后是同步路径）
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(fileUri);
            _isReady = true;
            _logger.LogInformation("MainWindow 已请求加载前端（同步路径）：{Uri}", fileUri);
        }
        else
        {
            // 异步路径：等 CoreWebView2InitializationCompleted 回调里 Navigate
            _logger.LogInformation("MainWindow 已存 pending URI，等待 CoreWebView2 异步初始化：{Uri}", fileUri);
        }
    }

    /// <summary>
    /// 跳转到前端路由（不重新加载整个页面，走 SPA 内部 hash 切换）。
    /// 关键修复：先 Show 窗口，再检查 _isReady；不再因 _isReady = false 而跳过 Show。
    /// </summary>
    public void NavigateToRoute(string route)
    {
        // 先 Show 窗口，确保用户能看到反馈
        if (!Visible) Show();
        BringToFront();
        Activate();

        // _isReady = false 时（WebView2 还没好），窗口已开但路由不跳转
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