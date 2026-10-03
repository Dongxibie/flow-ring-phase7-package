using System.IO;
using FlowRing.DesktopHost.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
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

        // 2. 显式解析 Runtime 路径（从注册表 BLBeacon 读版本号）
        var browserPath = ResolveWebView2RuntimePath();
        _logger.LogInformation("前端 dist 路径：{Path}", ResolveFrontendDistPath());
        if (!string.IsNullOrEmpty(browserPath))
        {
            _logger.LogInformation("WebView2 Runtime 显式路径：{Path}", browserPath);
        }
        else
        {
            _logger.LogWarning("无法从注册表解析 Runtime 路径，将让 SDK 自动查找（可能失败）");
        }

        // 3. 创建 Environment（失败则走暂停路径）
        try
        {
            _environment = !string.IsNullOrEmpty(browserPath)
                ? await CoreWebView2Environment.CreateAsync(browserPath, webView2Dir)
                : await CoreWebView2Environment.CreateAsync(webView2Dir);
            _logger.LogInformation("WebView2Environment 创建完成（user data: {Dir}）", webView2Dir);
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            _logger.LogWarning(ex, "WebView2 Runtime 缺失，进入暂停态，提示用户下载安装");
            _controller.SetPaused(true);
            return;
        }

        // 4. 解析前端 dist 路径
        var frontendDistPath = ResolveFrontendDistPath();

        // 5. 实例化 MainWindow（不 Show，等用户点菜单再 Show）
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

    /// <summary>
    /// 从注册表 BLBeacon 读 WebView2 Runtime 版本号，拼出 msedgewebview2.exe 完整路径。
    /// 同时尝试 64-bit 视图（HKLM\SOFTWARE\Microsoft）和 32-bit 兼容视图（WOW6432Node）。
    /// </summary>
    private static string? ResolveWebView2RuntimePath()
    {
        string? version = null;
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\EdgeWebView"))
            {
                if (key?.GetValue("BLBeacon") is string v64 && !string.IsNullOrEmpty(v64))
                {
                    version = v64;
                }
            }
            if (version is null)
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeWebView");
                if (key?.GetValue("BLBeacon") is string v32 && !string.IsNullOrEmpty(v32))
                {
                    version = v32;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
        if (version is null)
        {
            return null;
        }
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var path = Path.Combine(programFilesX86, "Microsoft", "EdgeWebView", "Application", version, "msedgewebview2.exe");
        return File.Exists(path) ? path : null;
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