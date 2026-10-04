using System.ComponentModel;
using System.Runtime.InteropServices;
using FlowRing.DesktopBridge.Abstractions;
using FlowRing.RingCore;
using FlowRing.RingCore.Geometry;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// Win32 WH_MOUSE_LL Hook 真实实现。
/// 识别鼠标侧键（XBUTTON1 / XBUTTON2）、中键、右键长按；其他按键 pass-through 给 CallNextHookEx。
/// 长按识别：按下 → 启动计时器 → 超过 150ms 触发 HoldDetected 事件。
/// </summary>
public sealed class MouseInputAdapter : IInputAdapter, IDisposable
{
    private const int HoldThresholdMs = 150;
    private const long TriggerWindowMs = 600; // 长按后再次按下若超过此间隔则视为新一次触发
    private static readonly TimeSpan HoldRepeatTimeout = TimeSpan.FromMilliseconds(TriggerWindowMs);

    private readonly ILogger<MouseInputAdapter> _logger;
    private readonly InputAdapterEvents _events = new();
    private readonly FullscreenDetector _fullscreen = new();
    private delegate nint HookProc(int nCode, nint wParam, nint lParam);
    private readonly HookProc _hookProcDelegate;
    private readonly HookProc _keyboardHookProcDelegate;
    private nint _hookHandle;
    private nint _keyboardHookHandle;
    private nint _moduleHandle;
    private readonly CancellationTokenSource _cts = new();

    // 触发态
    private int _pressedButtonVk;
    private NativeMethods.POINT _pressPoint;
    private long _pressTimestampMs;
    private bool _holdFired;
    private DateTimeOffset _lastReleaseAt = DateTimeOffset.MinValue;

    public MouseInputAdapter(ILogger<MouseInputAdapter> logger)
    {
        _logger = logger;
        _hookProcDelegate = HookCallback;
        _keyboardHookProcDelegate = KeyboardHookCallback;
    }

    public event EventHandler<SpatialIntentEvent>? IntentEmitted
    {
        add => _events.IntentEmitted += value;
        remove => _events.IntentEmitted -= value;
    }

    public event EventHandler<RawInputEvent>? RawInputEmitted
    {
        add => _events.RawInputEmitted += value;
        remove => _events.RawInputEmitted -= value;
    }

    /// <summary>v23：触发键释放（长按释放=执行方向；快速点按=驻留菜单）。</summary>
    public event EventHandler<InputReleasedEvent>? InputReleased
    {
        add => _events.InputReleased += value;
        remove => _events.InputReleased -= value;
    }

    /// <summary>v24：任意鼠标按钮按下（左/右键），用于"点击环外关闭"判定；不吞键。</summary>
    public event EventHandler<RawButtonEvent>? RawButtonDown
    {
        add => _events.RawButtonDown += value;
        remove => _events.RawButtonDown -= value;
    }

    /// <summary>v24：全局 ESC 按下（环显示期间宿主据此关闭覆盖层）。钩子永远放行 ESC。</summary>
    public event EventHandler? EscapePressed
    {
        add => _events.EscapePressed += value;
        remove => _events.EscapePressed -= value;
    }

    /// <summary>暂停/全屏保护：为 true 时触发键完全放行（不吞键、不发意图）。</summary>
    public bool IsSuspended { get; set; }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        _moduleHandle = NativeMethods.GetModuleHandleW(null);
        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL,
            Marshal.GetFunctionPointerForDelegate(_hookProcDelegate),
            _moduleHandle,
            0);

        if (_hookHandle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"SetWindowsHookEx(WH_MOUSE_LL) 失败: {error}");
        }

        _logger.LogInformation("WH_MOUSE_LL Hook 已安装 (handle=0x{Handle:X})", _hookHandle);

        // v24：低级键盘钩子——只为捕获 ESC（环显示时关闭覆盖层），永远 CallNextHookEx 不吞键
        _keyboardHookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL,
            Marshal.GetFunctionPointerForDelegate(_keyboardHookProcDelegate),
            _moduleHandle,
            0);
        if (_keyboardHookHandle == nint.Zero)
        {
            var kbError = Marshal.GetLastWin32Error();
            _logger.LogWarning("WH_KEYBOARD_LL Hook 安装失败({Error})：ESC 关闭退化为仅页面内生效", kbError);
        }
        else
        {
            _logger.LogInformation("WH_KEYBOARD_LL Hook 已安装 (handle=0x{Handle:X})", _keyboardHookHandle);
        }

        // HoldDetect 后台循环
        _ = Task.Run(HoldDetectLoopAsync, _cts.Token);

        await Task.CompletedTask;
    }

    public async Task UninstallAsync(CancellationToken ct)
    {
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
        if (_keyboardHookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = nint.Zero;
        }
        _logger.LogInformation("WH_MOUSE_LL Hook 已卸载");
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }
        if (_keyboardHookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = nint.Zero;
        }
        _cts.Cancel();
        _cts.Dispose();
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        var swallow = false;
        try
        {
            if (nCode >= 0)
            {
                swallow = ProcessMouseMessage(wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hook 回调异常");
        }
        // v22：侧键整段吞掉（不作浏览器前进/后退）；长按触发后的右键抬起也吞掉，
        // 避免底层应用弹出右键菜单与快捷环叠影
        return swallow ? 1 : NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>v24：低级键盘钩子回调——只上报 ESC 按下，永远放行（绝不吞键）。</summary>
    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    if (kb.vkCode == NativeMethods.VK_ESCAPE)
                    {
                        _events.EmitEscape();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "键盘钩子回调异常");
        }
        // ESC 永远放行：其他应用/游戏的 ESC 行为不受影响
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    /// <returns>true = 吞掉该事件，不传给底层应用。</returns>
    private bool ProcessMouseMessage(nint wParam, nint lParam)
    {
        var msg = (int)wParam;
        var raw = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

        _events.EmitRawInputRaw(raw.pt.X, raw.pt.Y, raw.time);

        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                // v24：左键按下——"点击环外关闭"判定用；不吞键
                _events.EmitRawButton(raw.pt.X, raw.pt.Y, false);
                return false;
            case NativeMethods.WM_XBUTTONDOWN when (raw.mouseData >> 16) == 1:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    LogSuppressedTriggerIfFullscreen();
                    return false; // 暂停/全屏保护：完全放行（不吞键、不发意图）
                }
                OnButtonDown(NativeMethods.VK_XBUTTON1, raw.pt, raw.time);
                EmitIntentAt(raw); // v23：侧键按下环即出现（不等 150ms）
                return true; // 侧键保留给 Flow Ring
            case NativeMethods.WM_XBUTTONDOWN when (raw.mouseData >> 16) == 2:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    LogSuppressedTriggerIfFullscreen();
                    return false; // 暂停/全屏保护：完全放行（不吞键、不发意图）
                }
                OnButtonDown(NativeMethods.VK_XBUTTON2, raw.pt, raw.time);
                EmitIntentAt(raw);
                return true;
            case NativeMethods.WM_MBUTTONDOWN:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    return false; // 暂停/全屏保护：完全放行（不吞键、不发意图）
                }
                OnButtonDown(NativeMethods.VK_MBUTTON, raw.pt, raw.time);
                return false;
            case NativeMethods.WM_RBUTTONDOWN:
                _events.EmitRawButton(raw.pt.X, raw.pt.Y, true); // v24：右键按下（环外关闭判定用）
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    return false; // 暂停/全屏保护：完全放行（不吞键、不发意图）
                }
                OnButtonDown(NativeMethods.VK_RBUTTON, raw.pt, raw.time);
                return false;

            case NativeMethods.WM_XBUTTONUP when _pressedButtonVk is NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    ClearPressState(); // 暂停/全屏保护：清掉按压残留，避免解除后补发意图或下一次点按被误判
                    return false; // 完全放行（不吞键、不发意图）
                }
                OnButtonUp(raw.pt);
                return true;
            case NativeMethods.WM_MBUTTONUP when _pressedButtonVk == NativeMethods.VK_MBUTTON:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    ClearPressState(); // 暂停/全屏保护：清掉按压残留，避免解除后补发意图或下一次点按被误判
                    return false; // 完全放行（不吞键、不发意图）
                }
                OnButtonUp(raw.pt);
                return false;
            case NativeMethods.WM_RBUTTONUP when _pressedButtonVk == NativeMethods.VK_RBUTTON:
                if (IsSuspended || _fullscreen.IsFullscreenForeground())
                {
                    ClearPressState(); // 暂停/全屏保护：清掉按压残留（含 _holdFired），避免下一次右键抬起被误吞
                    return false; // 完全放行（不吞键、不发意图）
                }
                var swallowUp = _holdFired; // 长按已触发：吞掉抬起，防右键菜单
                OnButtonUp(raw.pt);
                return swallowUp;
        }

        return false;
    }

    private void OnButtonDown(int vk, NativeMethods.POINT pt, uint rawTime)
    {
        if (_pressedButtonVk != 0)
        {
            return;
        }
        _pressedButtonVk = vk;
        _pressPoint = pt;
        _pressTimestampMs = Environment.TickCount64;
        _holdFired = false;
    }

    private void OnButtonUp(NativeMethods.POINT releasePt)
    {
        if (_pressedButtonVk == 0)
        {
            return;
        }

        // v23：释放事件——WasHold 由按压时长判定（≥150ms 为长按，执行方向选择；否则快速点按=驻留菜单）。
        // _holdFired 仅表示是否已发过意图（侧键按下即置 true），不再作为长按依据。
        if (_pressedButtonVk is NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2
            or NativeMethods.VK_MBUTTON or NativeMethods.VK_RBUTTON)
        {
            var wasHold = Environment.TickCount64 - _pressTimestampMs >= HoldThresholdMs;
            _events.EmitReleased(new InputReleasedEvent(
                releasePt.X,
                releasePt.Y,
                Environment.TickCount64,
                wasHold,
                _pressedButtonVk));
        }

        _pressedButtonVk = 0;
        _lastReleaseAt = DateTimeOffset.UtcNow;
    }

    /// <summary>清空当前按压状态，且不派发释放事件（暂停/全屏保护放行时使用）。</summary>
    private void ClearPressState()
    {
        _pressedButtonVk = 0;
        _pressTimestampMs = 0;
        _holdFired = false;
    }

    /// <summary>v24：触发被全屏保护吞掉时留下一行可诊断日志（只记侧键，避免游戏内点击刷屏）。</summary>
    private void LogSuppressedTriggerIfFullscreen()
    {
        if (!IsSuspended && _fullscreen.IsFullscreenForeground())
        {
            _logger.LogInformation("全屏保护：放行触发键（前台为全屏应用/窗口）");
        }
    }

    private void EmitIntentAt(NativeMethods.MSLLHOOKSTRUCT raw)
    {
        var triggerType = MapButtonToTrigger(_pressedButtonVk);
        if (triggerType == TriggerType.None)
        {
            return;
        }
        // v23：侧键按下即标记已发意图（HoldDetect 循环不再对侧键重复发）
        if (_pressedButtonVk is NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2)
        {
            _holdFired = true;
        }
        var evt = new SpatialIntentEvent(
            triggerType,
            new RingPoint(raw.pt.X, raw.pt.Y),
            1.0f,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ModifierState.None);
        _events.EmitIntent(evt);
    }

    private async Task HoldDetectLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            if (_pressedButtonVk != 0 && !_holdFired
                && _pressedButtonVk is not (NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2))
            {
                var elapsed = Environment.TickCount64 - _pressTimestampMs;
                // 暂停/全屏保护：跳过本轮，不创建意图（保持 _holdFired=false，解除后可重试）
                if (elapsed >= HoldThresholdMs
                    && !IsSuspended
                    && !_fullscreen.IsFullscreenForeground())
                {
                    var triggerType = MapButtonToTrigger(_pressedButtonVk);
                    if (triggerType != TriggerType.None)
                    {
                        var origin = new RingPoint(_pressPoint.X, _pressPoint.Y);
                        var evt = new SpatialIntentEvent(
                            triggerType,
                            origin,
                            1.0f,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            ModifierState.None);
                        _events.EmitIntent(evt);
                        _holdFired = true;
                    }
                }
            }
            try
            {
                await Task.Delay(16, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private static TriggerType MapButtonToTrigger(int vk) => vk switch
    {
        NativeMethods.VK_XBUTTON1 => TriggerType.MouseSideButton,
        NativeMethods.VK_XBUTTON2 => TriggerType.MouseSideButton,
        NativeMethods.VK_MBUTTON => TriggerType.MiddleButton,
        NativeMethods.VK_RBUTTON => TriggerType.RightButtonLongPress,
        _ => TriggerType.None,
    };
}

/// <summary>
/// 事件转发的内部容器。EventHandler 在构造时生成 delegate 链。
/// </summary>
internal sealed class InputAdapterEvents
{
    public event EventHandler<SpatialIntentEvent>? IntentEmitted;
    public event EventHandler<RawInputEvent>? RawInputEmitted;
    public event EventHandler<InputReleasedEvent>? InputReleased;
    public event EventHandler<RawButtonEvent>? RawButtonDown;
    public event EventHandler? EscapePressed;

    public void EmitIntent(SpatialIntentEvent evt) => IntentEmitted?.Invoke(this, evt);

    public void EmitReleased(InputReleasedEvent evt) => InputReleased?.Invoke(this, evt);

    public void EmitRawButton(int x, int y, bool isRight) => RawButtonDown?.Invoke(this, new RawButtonEvent(x, y, isRight));

    public void EmitEscape() => EscapePressed?.Invoke(this, EventArgs.Empty);
    public void EmitRawInputRaw(int x, int y, uint rawTime)
    {
        var ms = rawTime == 0 ? Environment.TickCount64 : rawTime;
        RawInputEmitted?.Invoke(this, new RawInputEvent(x, y, ms));
    }
}