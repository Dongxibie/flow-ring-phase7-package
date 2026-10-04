using System.Diagnostics;
using FlowRing.DesktopBridge.Win32;
using FlowRing.DesktopHost.UI;
using FlowRing.RingCore;
using FlowRing.RingCore.Action;
using FlowRing.RingCore.Geometry;
using FlowRing.RingCore.Profile;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowRing.DesktopHost.Host;

/// <summary>
/// 全局控制器：负责 paused 状态切换、激活、退出、跳转到 WebView2 页面。
/// 注入到 TrayIcon 与 WebView2Host。
///
/// v21：功能闭环——
/// 1. 订阅鼠标钩子的 SpatialIntentEvent：侧键长按 → 唤起快捷环弹窗（RingOverlayForm）；
/// 2. ExecuteActionAsync：前端 ACTION_TRIGGER / 弹窗触发的动作真执行
///    （open-frontend 打开主界面；key-ctrl-shift-t 启动终端；其余走 ActionDispatcher → Win32 执行器）。
/// </summary>
public sealed class HostController : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HostController> _logger;
    private readonly Win32DesktopBridge _bridge;
    private readonly Tray.TrayIcon _tray;
    private readonly WebView2Host _webView2;
    private readonly BridgeServer _pipeServer;
    private readonly ActionDispatcher _actions;
    private readonly FullscreenDetector _fullscreen = new();
    private RingOverlayForm? _ringOverlay;
    private int _ringSizePx = 500; // 环径，前端 RING_SIZE 消息实时更新
    private int _deadZonePx = 56;  // v24：释放死区半径（px），前端 DEAD_ZONE 消息实时更新
    // v23：跟随/释放状态（按住=手势模式：环追光标，松开执行方向）
    private bool _ringHolding;
    private long _lastFollowMs;
    private RingPoint? _pressOrigin; // v24：本次按下的起点（方向判定与相对偏移的原点）
    private volatile bool _isPaused;
    private volatile bool _isActive;
    private bool _disposed;

    public HostController(
        ILoggerFactory? loggerFactory = null,
        Tray.TrayIcon? tray = null,
        Win32DesktopBridge? bridge = null,
        WebView2Host? webView2 = null,
        BridgeServer? pipeServer = null)
    {
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        _loggerFactory = lf;
        _logger = lf.CreateLogger<HostController>();
        _bridge = bridge ?? new Win32DesktopBridge(lf);
        _tray = tray ?? new Tray.TrayIcon(this);
        _webView2 = webView2 ?? new WebView2Host(this, lf);
        _pipeServer = pipeServer ?? new BridgeServer(this, lf);
        _actions = new ActionDispatcher(_bridge.Executors);
    }

    public bool IsPaused => _isPaused;
    public bool IsActive => _isActive;

    public async Task StartAsync(CancellationToken ct)
    {
        // 必须先订阅：UiReady 在下面的 await 期间于 UI 线程（线程 1 STA）触发
        _webView2.UiReady += OnUiReady;
        await _bridge.InitializeAsync(ct).ConfigureAwait(false);
        _tray.Initialize();
        await _pipeServer.StartAsync(ct).ConfigureAwait(false);
        await _webView2.InitializeAsync(ct).ConfigureAwait(false);
        _isActive = true;
        _tray.UpdateIcon();

        // v21：侧键长按 → 快捷环弹窗（弹窗本体在 OnUiReady 里于 UI 线程创建）
        _bridge.Input.IntentEmitted += OnSpatialIntent;
        _bridge.Input.InputReleased += OnInputReleased;
        _bridge.Input.RawInputEmitted += OnRawInput;

        _logger.LogInformation("Host 启动完成，托盘图标已显示");
    }

    public void NavigateTo(string route)
    {
        _webView2.NavigateTo(route);
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        _bridge.Input.IsSuspended = paused; // 真暂停：输入适配器完全放行触发键
        if (paused)
        {
            // 暂停时收起快捷环，避免覆盖层继续响应
            _ringHolding = false;
            HideRingOverlay();
        }
        _logger.LogInformation("Paused 状态切换：{State}", paused ? "已暂停" : "运行中");
        _tray.UpdateIcon();
    }

    /// <summary>v21：前端/弹窗触发的动作真执行入口。v22：支持自定义动作参数（app-launch 的目标）。</summary>
    public async Task ExecuteActionAsync(string code, string? arg = null)
    {
        if (IsPaused)
        {
            _logger.LogInformation("已暂停，忽略动作");
            return;
        }
        _logger.LogInformation("执行动作：{Code}（arg={Arg}）", code, arg ?? "-");
        try
        {
            switch (code)
            {
                case "open-frontend":
                    // MVP 语义：打开 Flow Ring 主界面（后续可配置为任意前端应用）
                    ShowMainWindow();
                    return;

                case "app-launch":
                    // v22：自定义动作——启动应用 / 打开网址（UseShellExecute 通吃 exe 与 URL）
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        _logger.LogWarning("app-launch 缺少目标");
                        return;
                    }
                    Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
                    _logger.LogInformation("已启动：{Arg}", arg);
                    return;

                case "key-ctrl-shift-t":
                    // 语义是"打开终端"：直接启动 Windows Terminal（回退 PowerShell），而非全局热键
                    LaunchTerminal();
                    return;
            }

            var now = DateTimeOffset.UtcNow;
            var ctx = new ActionContext(
                "default",
                new global::FlowRing.RingCore.Profile.ApplicationContext("FlowRing", string.Empty, nint.Zero, now),
                now.ToUnixTimeMilliseconds());
            var result = await _actions.DispatchAsync(code, ctx, CancellationToken.None);
            _logger.LogInformation(
                "动作 {Code} 完成：Success={Success} Error={Error} {Ms}ms",
                code, result.Success, result.Error, result.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动作 {Code} 执行失败", code);
        }
    }

    /// <summary>v21：显示主界面（"打开前端"动作）。</summary>
    public void ShowMainWindow()
    {
        var mw = _webView2.MainWindow;
        if (mw is null)
        {
            _logger.LogWarning("主窗口未初始化，无法打开前端");
            return;
        }
        if (mw.InvokeRequired)
        {
            _ = mw.BeginInvoke(ShowMainWindow);
            return;
        }
        if (!mw.Visible)
        {
            mw.Show();
        }
        mw.BringToFront();
        mw.Activate();
        _logger.LogInformation("已打开前端主界面");
    }

    /// <summary>v22.2：调试通道——强制显示快捷环（验证透明渲染）。</summary>
    public void DebugShowRing()
    {
        _ringOverlay?.ShowRing(_ringSizePx);
    }

    /// <summary>v21：隐藏快捷环弹窗（OVERLAY_DONE / Deactivate 时调用）。</summary>
    public void HideRingOverlay()
    {
        _ringOverlay?.HideRing();
    }

    /// <summary>v22.2：前端上报环径（设置页滑杆/弹窗共用）。</summary>
    public void SetRingSize(int sizePx)
    {
        if (sizePx > 0 && sizePx != _ringSizePx)
        {
            _ringSizePx = sizePx;
            _logger.LogInformation("环径更新：{Size}px", sizePx);
        }
    }

    /// <summary>v24：前端上报释放死区半径（clamp 到 10..120px）。</summary>
    public void SetDeadZone(int px)
    {
        var clamped = Math.Clamp(px, 10, 120);
        if (clamped != _deadZonePx)
        {
            _deadZonePx = clamped;
            _logger.LogInformation("死区半径更新：{Px}px", clamped);
        }
    }

    private void OnUiReady(object? sender, EventArgs e)
    {
        // 在 UI 线程（线程 1 STA）创建并预加载弹窗——避免 RPC_E_CHANGED_MODE 线程模式冲突
        EnsureRingOverlay();
        _ = InitializeRingOverlayAsync();
    }

    private void EnsureRingOverlay()
    {
        if (_ringOverlay is not null)
        {
            return;
        }
        _ringOverlay = new RingOverlayForm(this, _loggerFactory, _webView2.FrontendDist, _webView2.Environment);
        _logger.LogInformation("快捷环弹窗已创建（隐藏预加载）");
    }

    private async Task InitializeRingOverlayAsync()
    {
        try
        {
            await _ringOverlay!.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快捷环弹窗预加载失败");
        }
    }

    private void OnSpatialIntent(object? sender, SpatialIntentEvent e)
    {
        if (IsPaused)
        {
            return;
        }
        // 全屏保护（双保险：输入适配器内已有一层）
        if (_fullscreen.IsFullscreenForeground())
        {
            return;
        }
        // v22：侧键/中键/右键长按全部唤起；但在 Flow Ring 自己前台时不弹
        //（应用内右键有自己的覆盖层，避免叠加）
        if (e.TriggerType == TriggerType.None || IsForegroundSelf())
        {
            return;
        }
        var origin = e.OriginPoint ?? new RingPoint(0, 0);
        _logger.LogInformation("快捷环触发（{Trigger}）：({X},{Y})",
            e.TriggerType, origin.X, origin.Y);
        _pressOrigin = e.OriginPoint; // v24：记录按下点（方向判定/相对偏移的原点）
        _ringHolding = true; // v23：进入手势模式（环追光标，松开执行方向）
        _ringOverlay?.ShowRing(_ringSizePx);
    }

    // v23：按住期间环以 lerp 追光标，并把光标相对按下点的偏移回给前端做扇区高亮
    private void OnRawInput(object? sender, RawInputEvent e)
    {
        if (!_ringHolding)
        {
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastFollowMs < 12)
        {
            return; // ~80Hz 节流
        }
        _lastFollowMs = now;
        _ringOverlay?.FollowCursor(e.RawX, e.RawY); // 先让环追上光标（移动窗口）
        var origin = _pressOrigin ?? new RingPoint(e.RawX, e.RawY);
        var dx = e.RawX - (int)origin.X;
        var dy = e.RawY - (int)origin.Y;
        _ringOverlay?.PostToPage(
            "{\"type\":\"FOLLOW\",\"dx\":" + dx + ",\"dy\":" + dy + "}");
    }

    // v23：松开——长按释放执行光标所在方向；快速点按进入驻留菜单模式
    private void OnInputReleased(object? sender, InputReleasedEvent e)
    {
        if (!_ringHolding)
        {
            return; // 驻留模式由前端自身交互（点击扇区/ESC）收尾
        }
        _ringHolding = false;

        if (!e.WasHold)
        {
            // 快速点按：环驻留，等待用户点击扇区（现有菜单模式）
            _logger.LogInformation("快速点按：快捷环驻留（菜单模式）");
            return;
        }

        // v24：以按下点为原点做死区/方向判定（释放点相对按下点）
        var origin = _pressOrigin ?? new RingPoint(e.ReleaseX, e.ReleaseY);
        var decision = ReleaseDecider.Decide(
            origin, new RingPoint(e.ReleaseX, e.ReleaseY), true, _deadZonePx);

        if (decision.Outcome == ReleaseOutcome.Cancel)
        {
            _logger.LogInformation("死区释放：取消快捷环");
            _ringOverlay?.HideRing();
            return;
        }

        if (decision.Outcome == ReleaseOutcome.Execute)
        {
            var dir = decision.Direction.ToString();
            var dx = e.ReleaseX - (int)origin.X;
            var dy = e.ReleaseY - (int)origin.Y;
            _logger.LogInformation("长按释放，方向：{Dir}（位移 {Dx},{Dy}）", dir, dx, dy);
            _ringOverlay?.PostToPage(
                "{\"type\":\"RELEASE_SELECT\",\"dir\":\"" + dir + "\"}");
        }
    }

    private static bool IsForegroundSelf()
    {
        var fg = GetForegroundWindow();
        _ = GetWindowThreadProcessId(fg, out var pid);
        return pid == Environment.ProcessId;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    /// <summary>接收 WebView2 端消息（Named Pipe 通道；前端 PROFILE_LIST 等占位）。</summary>
    public void OnWebMessage(string json)
    {
        _logger.LogInformation("WebView2 → Host message received, length={Len}", json.Length);
    }

    private void LaunchTerminal()
    {
        try
        {
            var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            Process.Start(psi);
            _logger.LogInformation("已启动 Windows Terminal");
        }
        catch (Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true });
            _logger.LogInformation("未找到 wt.exe，已启动 PowerShell");
        }
    }

    public void ExitApp()
    {
        _logger.LogInformation("收到退出请求，关闭 Host");
        Dispose();
        Application.Exit();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _bridge.Input.IntentEmitted -= OnSpatialIntent;
        _bridge.Input.InputReleased -= OnInputReleased;
        _bridge.Input.RawInputEmitted -= OnRawInput;
        _pipeServer.Dispose();
        _webView2.Dispose();
        _ringOverlay?.Dispose();
        _ringOverlay = null;
        _bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tray.Dispose();
    }
}
