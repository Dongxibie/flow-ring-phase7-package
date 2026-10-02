using System.IO;
using FlowRing.DesktopHost.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// WebView2 宿主：创建 Environment + 实例化 MainWindow + 暴露 NavigateTo。
/// </summary>
public sealed class WebView2Host : IDisposable
{
    private readonly HostController _controller;
    private readonly ILogger<WebView2Host> _logger;
    private CoreWebView2Environment? _environment;
    private MainWindow? _mainWindow;

    public bool IsInitialized { get; private set; }
    public MainWindow? MainWindow => _mainWindow;

    public WebView2Host(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WebView2Host>();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (IsInitialized) return;

        // 1. 创建 WebView2 user data 目录
        var webView2Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRing",
            "WebView2");
        Directory.CreateDirectory(webView2Dir);

        // 2. 创建 Environment（失败则走暂停路径）
        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(webView2Dir);
            _logger.LogInformation("WebView2Environment 创建完成（user data: {Dir}）", webView2Dir);
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            _logger.LogWarning(ex, "WebView2 Runtime 缺失，进入暂停态，提示用户下载安装");
            _controller.SetPaused(true);
            return;
        }

        // 3. 解析前端 dist 路径
        var frontendDistPath = ResolveFrontendDistPath();
        _logger.LogInformation("前端 dist 路径：{Path}", frontendDistPath);

        // 4. 实例化 MainWindow（不 Show，等用户点菜单再 Show）
        _mainWindow = new MainWindow(_controller);
        try
        {
            await _mainWindow.InitializeAsync(frontendDistPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow 初始化失败");
        }

        IsInitialized = true;
        _logger.LogInformation("WebView2Host + MainWindow 初始化链路完成");
    }

    public void NavigateTo(string route)
    {
        if (_mainWindow is null)
        {
            _logger.LogWarning("MainWindow 未初始化，跳转请求被忽略：{Route}", route);
            return;
        }
        _mainWindow.NavigateToRoute(route);
    }

    private static string ResolveFrontendDistPath()
    {
        // 优先按 cwd 向上找（dotnet run 时）
        var cwd = Directory.GetCurrentDirectory();
        var byCwd = Path.GetFullPath(Path.Combine(cwd, "packages", "web", "dist"));
        if (Directory.Exists(byCwd))
        {
            return byCwd;
        }

        // 否则按 BaseDirectory 向上找（dotnet publish 后）
        var baseDir = AppContext.BaseDirectory;
        var byBase = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..",
            "packages", "web", "dist"));
        return byBase;
    }

    private static bool IsMissingRuntime(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || (ex.GetType().FullName ?? "").Contains("WebView2RuntimeNotFound", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _mainWindow?.Dispose();
        _mainWindow = null;
        _environment = null;
        IsInitialized = false;
    }
}