using FlowRing.RingCore.Geometry;

namespace FlowRing.RingCore;

/// <summary>
/// 触发键释放后的处理结果：Park=驻留/菜单模式；Cancel=死区内取消；Execute=执行方向。
/// </summary>
public enum ReleaseOutcome
{
    Park,
    Cancel,
    Execute,
}

/// <summary>
/// 释放决策结果。Outcome=Execute 时 Direction 为实际方向，其余情况为 Direction.Center。
/// </summary>
public sealed record ReleaseDecision(ReleaseOutcome Outcome, Direction Direction);

/// <summary>
/// 释放决策纯逻辑（不引用任何 OS API，供单测直接覆盖）。
/// 快速点按释放 → Park；长按释放按方向解析：死区内 → Cancel，其余 → Execute。
/// </summary>
public static class ReleaseDecider
{
    public static ReleaseDecision Decide(RingPoint origin, RingPoint release, bool wasHold, float deadZoneRadiusPx)
    {
        if (!wasHold)
        {
            return new ReleaseDecision(ReleaseOutcome.Park, Direction.Center);
        }

        var direction = new DirectionResolver().Resolve(origin, release, deadZoneRadiusPx);
        return direction == Direction.Center
            ? new ReleaseDecision(ReleaseOutcome.Cancel, Direction.Center)
            : new ReleaseDecision(ReleaseOutcome.Execute, direction);
    }
}
