using FlowRing.RingCore.Geometry;
using FluentAssertions;
using Xunit;

namespace FlowRing.RingCore.Tests;

/// <summary>
/// ReleaseDecider 覆盖：快速点按驻留 / 长按原地取消 / 长按方向执行 / 死区边界。
/// </summary>
public sealed class ReleaseDeciderTests
{
    private const float DeadZone = 30f;

    [Fact]
    public void QuickTapParks()
    {
        // 快速点按：无论位移多远都进入驻留/菜单模式
        var decision = ReleaseDecider.Decide(
            new RingPoint(100f, 100f), new RingPoint(200f, 100f), wasHold: false, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Park);
        decision.Direction.Should().Be(Direction.Center);
    }

    [Fact]
    public void HoldInPlaceCancels()
    {
        var decision = ReleaseDecider.Decide(
            new RingPoint(100f, 100f), new RingPoint(110f, 100f), wasHold: true, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Cancel);
        decision.Direction.Should().Be(Direction.Center);
    }

    [Fact]
    public void HoldRight120PxExecutesRight()
    {
        var decision = ReleaseDecider.Decide(
            new RingPoint(0f, 0f), new RingPoint(120f, 0f), wasHold: true, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Execute);
        decision.Direction.Should().Be(Direction.Right);
    }

    [Fact]
    public void HoldTopLeftExecutesTopLeft()
    {
        var decision = ReleaseDecider.Decide(
            new RingPoint(0f, 0f), new RingPoint(-100f, -100f), wasHold: true, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Execute);
        decision.Direction.Should().Be(Direction.TopLeft);
    }

    [Fact]
    public void DeadZoneBoundaryJustInsideCancels()
    {
        // 29.9px < 30px：仍在死区内 → Cancel
        var decision = ReleaseDecider.Decide(
            new RingPoint(0f, 0f), new RingPoint(29.9f, 0f), wasHold: true, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Cancel);
    }

    [Fact]
    public void DeadZoneBoundaryExactlyAtRadiusExecutes()
    {
        // 恰好 30px：Resolve 用严格小于判定死区，等于半径不算 Cancel
        var decision = ReleaseDecider.Decide(
            new RingPoint(0f, 0f), new RingPoint(30f, 0f), wasHold: true, deadZoneRadiusPx: DeadZone);

        decision.Outcome.Should().Be(ReleaseOutcome.Execute);
        decision.Direction.Should().Be(Direction.Right);
    }
}
