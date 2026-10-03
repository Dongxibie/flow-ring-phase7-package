using System.IO;
using FlowRing.DesktopHost.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// WebView2 宿主：创建 Environment + 实例化 MainWindow + 暴露 NavigateTo。
/// v5 修复：ResolveWebView2RuntimePath → ResolveWebView2RuntimeFolder，返回 folder 路径而非 exe 路径；
/// 注册表显式读 64-bit + 32-bit 两个视图；加 Application 目录枚举兜底（不依赖注册表）。
///
/// v15 修复：MainWindow.InitializeAsync 完成后自动 Show（v14 之前用户必须手动点托盘菜单才看到主窗口）
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

        var webView2Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRing", "WebView2");
        Directory.CreateDirectory(webView2Dir);

        // 关键修复（v5）：folder 路径 + 不依赖 SDK 自动查找
        var browserFolder = ResolveWebView2RuntimeFolder();
        if (!string.IsNullOrEmpty(browserFolder))
        {
            _logger.LogInformation("WebView2 Runtime folder: {Path}", browserFolder);
        }
        else
        {
            _logger.LogWarning("无法解析 Runtime folder，将让 SDK 自动查找（用户机器上几乎必定失败）");
        }

        try
        {
            _environment = !string.IsNullOrEmpty(browserFolder)
                ? await CoreWebView2Environment.CreateAsync(browserFolder, webView2Dir)
                : await CoreWebView2Environment.CreateAsync(webView2Dir);
            _logger.LogInformation("WebView2Environment 创建完成（user data: {Dir}）", webView2Dir);
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            _logger.LogWarning(ex, "WebView2 Runtime 缺失，进入暂停态，提示用户下载安装");
            _controller.SetPaused(true);
            return;
        }

        // 解析前端 dist 路径
        var frontendDistPath = ResolveFrontendDistPath();
        _logger.LogInformation("前端 dist 路径：{Path}", frontendDistPath);

        // 实例化 MainWindow
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

        // v15 关键修复：MainWindow 初始化后自动 Show
        // v14 之前用户必须手动点托盘菜单才看到主窗口。
        // v15 自动 Show 让用户启动 host 后立即看到主窗口（截图即可）。
        try
        {
            _mainWindow?.Show();
            _logger.LogInformation("MainWindow 已自动 Show");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow 自动 Show 失败");
        }
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
    /// 解析 WebView2 Runtime 目录（v5 关键修复）。
    /// SDK CoreWebView2Environment.CreateAsync 第一参数语义是"目录"而非文件路径；
    /// v3 修复版（commit 991cc02）误传 msedgewebview2.exe 文件路径，仍报 WebView2RuntimeNotFoundException。
    ///
    /// 优先级：
    ///   1. HKLM\SOFTWARE\Microsoft\EdgeWebView\BLBeacon（64-bit 视图）
    ///   2. HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeWebView\BLBeacon（32-bit 视图）
    ///   3. HKLM\SOFTWARE\Microsoft\EdgeUpdate\ClientState\{GUID}\pv（64-bit 视图）
    ///   4. HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\{GUID}\pv（32-bit 视图）
    ///   5. 枚举 ProgramFilesX86\Microsoft\EdgeWebView\Application，按版本号字符串倒序找第一个含 msedgewebview2.exe 的目录（兜底，不依赖注册表）
    ///
    /// 返回 null 时调用方走 SDK 自动查找（已知在 64 位进程 + 64 位视图 pv 不同步时会失败）。
    /// </summary>
    private static string? ResolveWebView2RuntimeFolder()
    {
        const string AppSubpath = @"Microsoft\EdgeWebView\Application";
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var appRoot = Path.Combine(programFilesX86, AppSubpath);

        // 步骤 1-4：注册表读版本号
        var version = TryReadVersionFromRegistry();
        if (!string.IsNullOrEmpty(version))
        {
            var folder = Path.Combine(appRoot, version);
            if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, "msedgewebview2.exe")))
            {
                return folder;
            }
        }

        // 步骤 5：枚举 Application 目录（兜底，不依赖注册表）
        if (Directory.Exists(appRoot))
        {
            var candidates = Directory.EnumerateDirectories(appRoot)
                .Select(d => new DirectoryInfo(d))
                .Where(d => File.Exists(Path.Combine(d.FullName, "msedgewebview2.exe")))
                .OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count > 0)
            {
                return candidates[0].FullName;
            }
        }

        return null;
    }

    private static string? TryReadVersionFromRegistry()
    {
        // BLBeacon 优先（WebView2 Runtime 安装器写入的）
        const string blBeacon = @"SOFTWARE\Microsoft\EdgeWebView";
        if (ReadRegistryValue(blBeacon, "BLBeacon", RegistryView.Registry64) is { } v64Beacon)
            return v64Beacon;
        if (ReadRegistryValue(blBeacon, "BLBeacon", RegistryView.Registry32) is { } v32Beacon)
            return v32Beacon;

        // ClientState pv 兜底（EdgeUpdate 服务写入的）
        const string clientState = @"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        if (ReadRegistryValue(clientState, "pv", RegistryView.Registry64) is { } v64Pv)
            return v64Pv;
        if (ReadRegistryValue(clientState, "pv", RegistryView.Registry32) is { } v32Pv)
            return v32Pv;

        return null;
    }

    private static string? ReadRegistryValue(string subKey, string valueName, RegistryView view)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFrontendDistPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        var byCwd = Path.GetFullPath(Path.Combine(cwd, "packages", "web", "dist"));
        if (Directory.Exists(byCwd)) return byCwd;

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