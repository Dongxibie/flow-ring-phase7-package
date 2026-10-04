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
///
/// v16 修复：保存 ILoggerFactory 实例化时传给 MainWindow，
/// 否则 MainWindow 内部用 NullLoggerFactory，所有 log 全被吞。
/// </summary>
public sealed class WebView2Host : IDisposable
{
    private readonly HostController _controller;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WebView2Host> _logger;
    private CoreWebView2Environment? _environment;
    private MainWindow? _mainWindow;

    public bool IsInitialized { get; private set; }
    public MainWindow? MainWindow => _mainWindow;

    /// <summary>v22.1：前端 dist 路径（快捷环弹窗的虚拟主机映射需要同一份）。</summary>
    public string FrontendDist { get; private set; } = string.Empty;

    /// <summary>v24：共享的 WebView2 Environment（弹窗复用同一 user data 目录，避免多环境冲突）。</summary>
    public CoreWebView2Environment? Environment => _environment;

    /// <summary>v21：MainWindow UI 线程就绪（CoreWebView2 初始化完成）后触发。</summary>
    public event EventHandler? UiReady;

    public WebView2Host(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;  // v16 存
        _logger = _loggerFactory.CreateLogger<WebView2Host>();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (IsInitialized) return;

        // v19 诊断：确认本方法跑在哪个线程（HostController.StartAsync 对本调用加了 ConfigureAwait(false)）
        _logger.LogInformation(
            "WebView2Host.InitializeAsync 进入（线程 {ThreadId}，apartment {Apt}）",
            System.Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState());

        var webView2Dir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
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
            // v19 诊断：CreateAsync await 之后的续体在哪个线程？
            _logger.LogInformation(
                "CreateAsync 后续线程 {ThreadId}，apartment {Apt}",
                System.Environment.CurrentManagedThreadId,
                Thread.CurrentThread.GetApartmentState());
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            _logger.LogWarning(ex, "WebView2 Runtime 缺失，进入暂停态，提示用户下载安装");
            _controller.SetPaused(true);
            return;
        }

        // v24：CreateAsync 成功后、控件初始化之前，迁移旧默认环境的用户数据
        MigrateLegacyUserData(webView2Dir);

        // 解析前端 dist 路径
        var frontendDistPath = ResolveFrontendDistPath();
        FrontendDist = frontendDistPath;
        _logger.LogInformation("前端 dist 路径：{Path}", frontendDistPath);

        // v16 关键修复：实例化 MainWindow 时传 loggerFactory
        // 之前 _mainWindow = new MainWindow(_controller) → loggerFactory=null → NullLogger → 所有 log 被吞
        _mainWindow = new MainWindow(_controller, _loggerFactory);
        _mainWindow.UiReady += (_, _) => UiReady?.Invoke(this, EventArgs.Empty);
        try
        {
            await _mainWindow.InitializeAsync(_environment, frontendDistPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MainWindow 初始化失败");
        }

        IsInitialized = true;
        _logger.LogInformation("WebView2Host + MainWindow 初始化链路完成");

        // v15：MainWindow 自动 Show
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

    private static string? ResolveWebView2RuntimeFolder()
    {
        const string AppSubpath = @"Microsoft\EdgeWebView\Application";
        var programFilesX86 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
        var appRoot = Path.Combine(programFilesX86, AppSubpath);

        var version = TryReadVersionFromRegistry();
        if (!string.IsNullOrEmpty(version))
        {
            var folder = Path.Combine(appRoot, version);
            if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, "msedgewebview2.exe")))
            {
                return folder;
            }
        }

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
        const string blBeacon = @"SOFTWARE\Microsoft\EdgeWebView";
        if (ReadRegistryValue(blBeacon, "BLBeacon", RegistryView.Registry64) is { } v64Beacon)
            return v64Beacon;
        if (ReadRegistryValue(blBeacon, "BLBeacon", RegistryView.Registry32) is { } v32Beacon)
            return v32Beacon;

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

    /// <summary>
    /// v24：把旧默认环境数据（exe 目录下 "FlowRing.DesktopHost.exe.WebView2\EBWebView"）
    /// 迁移到新的 user data 目录（LocalAppData\FlowRing\WebView2\EBWebView）。
    /// 仅当源存在且目标不存在时执行；先复制到 "EBWebView.migrating" 再 Move 成 EBWebView；
    /// 任何失败只记 Warning，不影响启动；旧目录保留不删。
    /// </summary>
    private void MigrateLegacyUserData(string webView2Dir)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "FlowRing.DesktopHost.exe.WebView2", "EBWebView");
        var target = Path.Combine(webView2Dir, "EBWebView");
        var temp = Path.Combine(webView2Dir, "EBWebView.migrating");

        if (!Directory.Exists(source) || Directory.Exists(target))
        {
            return;
        }

        try
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true); // 上次迁移失败的残留
            }
            CopyDirectory(source, temp);
            Directory.Move(temp, target);
            _logger.LogInformation("旧 WebView2 用户数据已迁移：{Source} → {Target}", source, target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "旧 WebView2 用户数据迁移失败（忽略，不影响启动）");
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch
            {
                // 清理失败同样不影响启动
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    public void Dispose()
    {
        _mainWindow?.Dispose();
        _mainWindow = null;
        _environment = null;
        IsInitialized = false;
    }
}