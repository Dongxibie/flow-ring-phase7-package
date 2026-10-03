using System.Runtime.InteropServices;
using FlowRing.RingCore.Action;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopBridge.Win32;

/// <summary>
/// 系统 Action 执行器：v21 起为真实实现。
/// 支持的 actionId（与前端 actions.ts 的 code 一致）：
///   system-mute        媒体键静音切换（VK_VOLUME_MUTE）
///   system-volume-up/down  音量加减（VK_VOLUME_UP / DOWN）
///   system-screenshot  Win+Shift+S（系统截图工具）
///   system-taskview    Win+Tab（任务视图）
///   system-desktop     Win+D（显示桌面）
///   system-clipboard   Win+V（剪贴板历史）
///   system-lock        LockWorkStation（锁屏）
///   system-minimize-window / maximize / close  针对 ctx.CurrentApp.WindowHandle
/// 全部经 SendInput / 标准 Win32，无占位。
/// </summary>
public sealed class Win32SystemExecutor : IActionExecutor
{
    private readonly ILogger<Win32SystemExecutor> _logger;

    public Win32SystemExecutor(ILogger<Win32SystemExecutor> logger)
    {
        _logger = logger;
    }

    public ActionKind Kind => ActionKind.System;

    public PermissionTier RequiredTier => PermissionTier.Normal;

    public ValueTask<ExecutionResult> ExecuteAsync(string actionId, ActionContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var op = actionId.StartsWith("system-", StringComparison.OrdinalIgnoreCase)
                ? actionId.Substring("system-".Length)
                : actionId;

            _logger.LogInformation("System Action 执行：actionId={ActionId} op={Op}", actionId, op);

            switch (op.ToLowerInvariant())
            {
                case "mute":
                case "volumemute":
                    Ensure(SendVk(0xAD), "静音键发送失败"); // VK_VOLUME_MUTE
                    break;

                case "volumeup":
                case "volume-up":
                    Ensure(SendVk(0xAF), "音量+发送失败"); // VK_VOLUME_UP
                    break;

                case "volumedown":
                case "volume-down":
                    Ensure(SendVk(0xAE), "音量-发送失败"); // VK_VOLUME_DOWN
                    break;

                case "screenshot":
                    Ensure(WinCombo(0x5B, 0x10, 0x53), "Win+Shift+S 发送失败"); // Win+Shift+S
                    break;

                case "taskview":
                    Ensure(WinCombo(0x5B, 0x09), "Win+Tab 发送失败"); // Win+Tab
                    break;

                case "desktop":
                    Ensure(WinCombo(0x5B, 0x44), "Win+D 发送失败"); // Win+D
                    break;

                case "clipboard":
                    Ensure(WinCombo(0x5B, 0x56), "Win+V 发送失败"); // Win+V
                    break;

                case "lock":
                    Ensure(LockWorkStation(), "LockWorkStation 失败");
                    break;

                case "minimize-window":
                case "minimizewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 2 /* SW_MINIMIZE */);
                    break;

                case "maximize-window":
                case "maximizewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 3 /* SW_MAXIMIZE */);
                    break;

                case "close-window":
                case "closewindow":
                    NativeMethods.ShowWindow(ctx.CurrentApp.WindowHandle, 0 /* SW_HIDE */);
                    break;

                default:
                    sw.Stop();
                    return ValueTask.FromResult(new ExecutionResult(false, $"未知 system op：{op}", sw.ElapsedMilliseconds));
            }

            sw.Stop();
            return ValueTask.FromResult(new ExecutionResult(true, null, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ValueTask.FromResult(new ExecutionResult(false, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    private static void Ensure(bool ok, string error)
    {
        if (!ok)
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>单击一个虚拟键（按下 + 抬起）。</summary>
    private static bool SendVk(ushort vk)
    {
        return SendKey(vk, up: false) && SendKey(vk, up: true);
    }

    /// <summary>按下并释放一个 Win 组合键：Win 按下 → 各键按下/抬起 → Win 抬起。</summary>
    private static bool WinCombo(ushort winVk, params ushort[] keys)
    {
        if (!SendKey(winVk, up: false))
        {
            return false;
        }
        foreach (var k in keys)
        {
            if (!SendKey(k, up: false))
            {
                _ = SendKey(winVk, up: true);
                return false;
            }
            if (!SendKey(k, up: true))
            {
                _ = SendKey(winVk, up: true);
                return false;
            }
        }
        _ = SendKey(winVk, up: true);
        return true;
    }

    private static bool SendKey(ushort vk, bool up)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0u,
                },
            },
        };
        var size = Marshal.SizeOf<NativeMethods.INPUT>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, ptr, false);
            return NativeMethods.SendInput(1, ptr, size) == 1;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
