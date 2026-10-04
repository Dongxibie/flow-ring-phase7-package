using FlowRing.RingCore;
using FlowRing.RingCore.Action;
using FlowRing.RingCore.Profile;

namespace FlowRing.DesktopBridge.Abstractions;

/// <summary>
/// OS 隔离层根接口。所有平台特定实现都要实现它。
/// </summary>
public interface IDesktopBridge : IAsyncDisposable
{
    IInputAdapter Input { get; }
    IContextDetector Context { get; }
    IReadOnlyDictionary<ActionKind, IActionExecutor> Executors { get; }
    ISystemInfo System { get; }

    Task InitializeAsync(CancellationToken ct);
}

/// <summary>
/// 设备专属输入适配器。Win32 实现走 WH_MOUSE_LL / WH_KEYBOARD_LL Hook。
/// </summary>
public interface IInputAdapter : IAsyncDisposable
{
    event EventHandler<SpatialIntentEvent>? IntentEmitted;
    event EventHandler<RawInputEvent>? RawInputEmitted;

    /// <summary>v23：触发键释放（长按释放=执行方向；快速点按=驻留菜单）。</summary>
    event EventHandler<InputReleasedEvent>? InputReleased;

    /// <summary>v24：任意鼠标按钮按下（左/右键）。用于"点击环外关闭"判定；不吞键。</summary>
    event EventHandler<RawButtonEvent>? RawButtonDown;

    /// <summary>v24：全局 ESC 按下（环显示期间宿主据此关闭覆盖层）。钩子永远不吞 ESC。</summary>
    event EventHandler? EscapePressed;

    /// <summary>暂停/全屏保护：为 true 时触发键完全放行（不吞键、不发意图）。</summary>
    bool IsSuspended { get; set; }

    Task InstallAsync(CancellationToken ct);
    Task UninstallAsync(CancellationToken ct);
}

/// <summary>
/// 上下文检测器。Win32 版本走 GetForegroundWindow。
/// </summary>
public interface IContextDetector
{
    Task<ApplicationContext> DetectAsync(CancellationToken ct);
}

/// <summary>
/// 系统信息。用于 Profile 切换触发。
/// </summary>
public interface ISystemInfo
{
    string Platform { get; }
    string OSVersion { get; }
    string AppVersion { get; }
    string UserDataPath { get; }
}