using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceLifetimeShutdownTests
{
    [Fact]
    public void SignalOnlyShutdownNeverWaitsForBlockedEvaluation()
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.shutdown")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        var registration = registry.Register(
            definition,
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        var stopwatch = Stopwatch.StartNew();
        registration.Dispose();
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.True(runner.Snapshot.Handoff.StopRequested);
        Assert.NotEqual(ServiceHandoffPhase.Stopped, runner.Snapshot.Handoff.Phase);

        release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.Handoff.Phase == ServiceHandoffPhase.Stopped,
            TimeSpan.FromSeconds(2)));
        Assert.True(definition.ResourcesReleased.Wait(TimeSpan.FromSeconds(2)));
        registry.Dispose();
    }

    [Fact]
    public void StoppedHandoffDoesNotRetainPinnedConfiguration()
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.shutdown-config")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        var registration = RegisterWithPayload(registry, definition, out var reference);
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        registration.Dispose();
        release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => runner.ProbeHandoff().Phase == ServiceHandoffPhase.Stopped,
            TimeSpan.FromSeconds(2)));

        ForceCollection(reference);
        Assert.False(reference.IsAlive);
        GC.KeepAlive(runner);
        registry.Dispose();
    }

    private static void ForceCollection(WeakReference reference)
    {
        for (var attempt = 0; attempt < 8 && reference.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ServiceRegistration<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction>
        RegisterWithPayload(
            ServiceCycleRegistry registry,
            ExecutionServiceDefinition definition,
            out WeakReference reference)
    {
        var payload = new ActionPayload(91);
        reference = new WeakReference(payload);
        return registry.Register(definition, new ExecutionConfig(1, payload), new LifecycleGeneration(1));
    }
}
