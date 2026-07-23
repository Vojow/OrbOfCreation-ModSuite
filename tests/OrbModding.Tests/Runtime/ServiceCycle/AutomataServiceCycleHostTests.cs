using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutomataServiceCycleHostTests
{
    [Fact]
    public void OneHostPumpsDifferentlyTypedServicesAndOwnsSuiteLifecycle()
    {
        var frame = 1L;
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(7),
            clock);
        var execution = new ExecutionServiceDefinition("host.execution");
        var synthetic = new SyntheticServiceDefinition("host.synthetic");
        using var executionRegistration = registry.Register(
            execution,
            new ExecutionConfig(1),
            new LifecycleGeneration(7));
        using var syntheticRegistration = registry.Register(
            synthetic,
            new SyntheticConfig(2),
            new LifecycleGeneration(7));
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: null);

        var first = host.Tick();

        Assert.True(first.Accepted);
        Assert.Equal(2, first.CapturesAttempted);
        Assert.Equal(1, execution.CaptureCount);
        Assert.Equal(1, synthetic.FrameCreateCount);
        Assert.Equal(2, host.Pump.ServiceCapacity);
        Assert.False(host.Tick().Accepted);
        Assert.Equal(1, execution.CaptureCount);
        Assert.Equal(1, synthetic.FrameCreateCount);
        Assert.True(host.TryReplaceLifecycle(8));
        Assert.Equal(new LifecycleGeneration(8), host.CurrentLifecycle);
        Assert.False(host.TryReplaceLifecycle(8));
    }

    [Fact]
    public void ClaimedPumpShutdownRunsExactlyOnce()
    {
        var calls = 0;
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            new SyntheticServiceDefinition("host.shutdown"),
            new SyntheticConfig(1),
            new LifecycleGeneration(1));
        var host = new AutomataServiceCycleHost(
            registry,
            () => 1,
            pumpTiming: null,
            semanticTrace: null);
        host.ClaimPumpShutdown(() =>
        {
            calls++;
            host.Pump.Dispose();
            return true;
        });

        host.Dispose();
        host.Dispose();

        Assert.Equal(1, calls);
    }
}
