using System.Runtime.InteropServices;
using System.Text;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// 全屏游戏/演示保护检测：前台窗口完全覆盖显示器且无标题栏（或无边框弹窗）时视为全屏。
/// 结果缓存 500ms，避免每条鼠标消息都做多次 Win32 查询。
/// </summary>
public sealed class FullscreenDetector
{
    private const int CacheTtlMs = 500;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int GWL_STYLE = -16;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    private static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd" };

    private long _lastCheckTickMs = -1;
    private bool _lastResult;

    /// <summary>前台窗口是否为全屏（结果缓存 500ms）。</summary>
    /// <remarks>
    /// fail-open：本检测是“保护性增强”，不是触发链路的必要条件。它由 WH_MOUSE_LL 钩子回调
    /// （MouseInputAdapter.ProcessMouseMessage 的触发键分支）同步调用，一旦异常逃逸就会打断
    /// 按键处理、导致触发键失效（v23.1 即因 P/Invoke 签名 UB 在此链路崩溃）。因此任何可捕获
    /// 异常一律吞掉并返回 false（按“非全屏”放行），宁可漏判全屏，也不让检测器阻塞或炸掉触发链路。
    /// </remarks>
    public bool IsFullscreenForeground()
    {
        try
        {
            var now = Environment.TickCount64;
            if (_lastCheckTickMs >= 0 && now - _lastCheckTickMs < CacheTtlMs)
            {
                return _lastResult;
            }

            _lastCheckTickMs = now;
            _lastResult = Detect();
            return _lastResult;
        }
        catch (Exception)
        {
            // fail-open（理由同上）：失败也要写回缓存，避免每次鼠标消息都重复踩异常路径。
            _lastCheckTickMs = Environment.TickCount64;
            _lastResult = false;
            return false;
        }
    }

    /// <summary>窗口矩形 (l,t,r,b) 是否完全覆盖显示器矩形 (ml,mt,mr,mb)。</summary>
    public static bool CoversMonitor(int l, int t, int r, int b, int ml, int mt, int mr, int mb)
    {
        return l <= ml && t <= mt && r >= mr && b >= mb;
    }

    /// <summary>样式是否表明无边框：无 WS_CAPTION，或带 WS_POPUP。</summary>
    public static bool StyleIndicatesBorderless(int style)
    {
        return (style & WS_CAPTION) == 0 || (style & WS_POPUP) != 0;
    }

    private static bool Detect()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            return false;
        }

        // 桌面 / 任务栏等外壳窗口不算全屏
        if (IsShellWindow(hwnd))
        {
            return false;
        }

        // 3 = QUNS_RUNNING_D3D_FULL_SCREEN：独占全屏 D3D 应用直接判定。
        // 必须先验 HRESULT：仅 S_OK(0) 时出参才有效（失败时 state 无意义，不能拿去比较）。
        var hr = SHQueryUserNotificationState(out var notificationState);
        if (hr == 0 && notificationState == QUNS_RUNNING_D3D_FULL_SCREEN)
        {
            return true;
        }

        if (!GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero)
        {
            return false;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return false;
        }

        if (!CoversMonitor(
                windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom,
                monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right, monitorInfo.rcMonitor.Bottom))
        {
            return false;
        }

        // 有标题栏的最大化窗口不算全屏
        return StyleIndicatesBorderless(GetWindowLongW(hwnd, GWL_STYLE));
    }

    private static bool IsShellWindow(nint hwnd)
    {
        var className = new StringBuilder(64);
        if (GetClassNameW(hwnd, className, className.Capacity) == 0)
        {
            return false;
        }

        var name = className.ToString();
        foreach (var shellClass in ShellWindowClasses)
        {
            if (string.Equals(name, shellClass, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        internal int cbSize;
        internal RECT rcMonitor;
        internal RECT rcWork;
        internal uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    // HRESULT SHQueryUserNotificationState(QUERY_USER_NOTIFICATION_STATE *pquns)
    // 真实原型必须带 out 指针参数：无参声明时 Win32 仍会向该出参地址写结果，等价于写野指针
    // → System.AccessViolationException（v23.1 每次触发键按下崩溃的根因）。
    // 返回 int 即原始 HRESULT（DllImport 默认 PreserveSig=true，不做 HRESULT→异常 转换），
    // 因此调用方必须自行判 S_OK(0)。
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowLongW(nint hWnd, int nIndex);
}
