using System.Diagnostics;
using System.Globalization;
using FlowRing.RingCore;
using FlowRing.RingCore.Geometry;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FlowRing.RingCore.Tests;

/// <summary>
/// RingCore 性能 benchmark：按骨架 Done Criteria 第 5 条验证 P95 时延。
/// MVP 阶段跑多次取 P95。
/// </summary>
public sealed class PerformanceBenchmarks
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DirectionResolverP50LatencyBelow16ms()
    {
        var resolver = new DirectionResolver();
        var center = new RingPoint(0f, 0f);
        var sw = new Stopwatch();

        for (int i = 0; i < 100; i++)
        {
            _ = resolver.Resolve(center, new RingPoint(i % 100f, i % 50f));
        }

        var samples = new List<double>(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            var x = (i * 13) % 200 - 100;
            var y = (i * 17) % 200 - 100;
            sw.Restart();
            _ = resolver.Resolve(center, new RingPoint(x, y));
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p50 = samples[samples.Count / 2];
        var p95 = samples[(int)(samples.Count * 0.95)];
        _output.WriteLine(string.Format(CultureInfo.InvariantCulture, "DirectionResolver P50={0:F4}ms P95={1:F4}ms", p50, p95));
        p50.Should().BeLessThan(16.0);
    }

    [Fact]
    public void InputStateMachineTransitionP95Below1ms()
    {
        var sm = new InputStateMachine();
        var sw = new Stopwatch();

        for (int i = 0; i < 1000; i++)
        {
            sm.Reset();
            sm.Transition(InputState.Pressed);
            sm.Transition(InputState.HoldDetected);
        }

        var samples = new List<double>(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            sm.Reset();
            sw.Restart();
            sm.Transition(InputState.Pressed);
            sm.Transition(InputState.HoldDetected);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)(samples.Count * 0.95)];
        _output.WriteLine(string.Format(CultureInfo.InvariantCulture, "InputStateMachine 两次转换 P95={0:F4}ms", p95));
        p95.Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task ActionDispatchP95Below50ms()
    {
        var registry = new FlowRing.RingCore.Action.MemoryActionRegistry();
        await registry.UpsertAsync(new FlowRing.RingCore.Action.ActionDef("key-t", FlowRing.RingCore.Action.ActionKind.Keyboard, "Type T", FlowRing.RingCore.Action.PermissionTier.Safe, "{}"), default);

        var executor = new TestExecutor();
        var engine = new FlowRing.RingCore.Action.ActionEngine(registry, new Dictionary<FlowRing.RingCore.Action.ActionKind, FlowRing.RingCore.Action.IActionExecutor>
        {
            [FlowRing.RingCore.Action.ActionKind.Keyboard] = executor,
        });

        var ctx = new FlowRing.RingCore.Action.ActionContext("default", new FlowRing.RingCore.Profile.ApplicationContext("test", "t", 0, DateTimeOffset.UtcNow), 0);

        for (int i = 0; i < 50; i++)
        {
            await engine.ExecuteAsync("key-t", ctx, FlowRing.RingCore.Action.PermissionTier.Safe, default);
        }

        var samples = new List<double>(1000);
        for (int i = 0; i < 1000; i++)
        {
            var sw = Stopwatch.StartNew();
            await engine.ExecuteAsync("key-t", ctx, FlowRing.RingCore.Action.PermissionTier.Safe, default);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)(samples.Count * 0.95)];
        _output.WriteLine(string.Format(CultureInfo.InvariantCulture, "ActionEngine end-to-end P95={0:F4}ms", p95));
        p95.Should().BeLessThan(50.0);
    }

    private sealed class TestExecutor : FlowRing.RingCore.Action.IActionExecutor
    {
        public FlowRing.RingCore.Action.ActionKind Kind => FlowRing.RingCore.Action.ActionKind.Keyboard;
        public FlowRing.RingCore.Action.PermissionTier RequiredTier => FlowRing.RingCore.Action.PermissionTier.Safe;
        public ValueTask<FlowRing.RingCore.Action.ExecutionResult> ExecuteAsync(string actionId, FlowRing.RingCore.Action.ActionContext ctx, CancellationToken ct)
            => ValueTask.FromResult(new FlowRing.RingCore.Action.ExecutionResult(true, null, 1));
    }
}