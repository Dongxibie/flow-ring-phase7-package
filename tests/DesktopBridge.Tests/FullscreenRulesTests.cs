using FlowRing.DesktopBridge.Win32;
using FluentAssertions;
using Xunit;

namespace FlowRing.DesktopBridge.Tests;

/// <summary>
/// FullscreenDetector 纯规则测试（不依赖真实窗口与系统状态）。
/// </summary>
public sealed class FullscreenRulesTests
{
    // -- CoversMonitor：窗口矩形 (l,t,r,b) 完全覆盖显示器 (ml,mt,mr,mb) --

    [Fact]
    public void CoversMonitorWhenRectMatchesMonitorExactly()
    {
        FullscreenDetector.CoversMonitor(0, 0, 1920, 1080, 0, 0, 1920, 1080).Should().BeTrue();
    }

    [Fact]
    public void CoversMonitorWhenRectExceedsMonitor()
    {
        // 无边框全屏窗口常因隐藏边框略大于显示器
        FullscreenDetector.CoversMonitor(-8, -8, 1928, 1088, 0, 0, 1920, 1080).Should().BeTrue();
    }

    [Fact]
    public void DoesNotCoverWhenRectSmallerThanMonitor()
    {
        FullscreenDetector.CoversMonitor(100, 100, 800, 600, 0, 0, 1920, 1080).Should().BeFalse();
    }

    [Fact]
    public void DoesNotCoverWhenRectIsOnAnotherMonitor()
    {
        FullscreenDetector.CoversMonitor(1920, 0, 3840, 1080, 0, 0, 1920, 1080).Should().BeFalse();
    }

    [Fact]
    public void DoesNotCoverWhenOnlyOneEdgeReachesMonitor()
    {
        // 左、上贴边但右、下未达显示器边界
        FullscreenDetector.CoversMonitor(0, 0, 1919, 1079, 0, 0, 1920, 1080).Should().BeFalse();
    }

    // -- StyleIndicatesBorderless：无 WS_CAPTION 或带 WS_POPUP 视为无边框 --

    [Theory]
    [InlineData(0)] // 无任何样式
    [InlineData(0x00040000)] // WS_THICKFRAME，无标题栏
    [InlineData(unchecked((int)0x80000000))] // WS_POPUP
    [InlineData(0x00CF0000 | unchecked((int)0x80000000))] // WS_POPUP 带标题栏样式仍按弹窗处理
    public void StyleIndicatesBorderlessForBorderlessStyles(int style)
    {
        FullscreenDetector.StyleIndicatesBorderless(style).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x00C00000)] // 仅 WS_CAPTION
    [InlineData(0x00C00000 | 0x10000000)] // WS_CAPTION | WS_VISIBLE
    [InlineData(0x00CF0000)] // WS_OVERLAPPEDWINDOW（含 WS_CAPTION）
    public void StyleIndicatesCaptionForWindowedStyles(int style)
    {
        FullscreenDetector.StyleIndicatesBorderless(style).Should().BeFalse();
    }

    [Fact]
    public void MaximizedWindowWithCaptionIsNotFullscreenBorderless()
    {
        // 最大化但有标题栏(WS_CAPTION)的普通窗口即使铺满显示器也不算全屏
        const int maximizedWindowStyle = 0x00CF0000; // WS_OVERLAPPEDWINDOW
        FullscreenDetector.CoversMonitor(-8, -8, 1928, 1088, 0, 0, 1920, 1080).Should().BeTrue();
        FullscreenDetector.StyleIndicatesBorderless(maximizedWindowStyle).Should().BeFalse();
    }
}
