using System.IO;
using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// 主窗口：WebView2 的唯一宿主，承载前端 SPA。
/// 关闭按钮只 Hide（不退出 host），退出走托盘菜单。
/// </summary>
public sealed class MainWindow : Form
{
    private readonly HostController _controller;
    private readonly ILogger<MainWindow> _logger;
    private readonly WebView2 _webView = new();
    private bool _isReady;

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
            _isReady = false;
            return;
        }

        var fileUri = new Uri(indexPath).AbsoluteUri;
        _webView.CoreWebView2.Navigate(fileUri);
        _isReady = true;
        _logger.LogInformation("MainWindow 已加载前端：{Uri}", fileUri);
    }

    /// <summary>
    /// 跳转到前端路由（不重新加载整个页面，走 SPA 内部 hash 切换）。
    /// </summary>
    public void NavigateToRoute(string route)
    {
        if (!_isReady || _webView.CoreWebView2 is null)
        {
            _logger.LogWarning("WebView2 未就绪，跳转请求被忽略：{Route}", route);
            return;
        }

        try
        {
            _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.location.hash = '#/{route}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToRoute 失败：{Route}", route);
            return;
        }

        if (!Visible) Show();
        BringToFront();
        Activate();
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