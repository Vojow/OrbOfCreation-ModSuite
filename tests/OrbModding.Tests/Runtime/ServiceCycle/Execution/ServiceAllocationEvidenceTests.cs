using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

[Trait("Category", "PerformanceSimulation")]
public sealed class ServiceAllocationEvidenceTests
{
    [Fact]
    public void WarmedIdleAppendDrainAndReceiptPathsAllocateNothingOnTheirOwningThreads()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock, measureWorkerAllocations: true);
        var definition = new ExecutionServiceDefinition("test.execution.alloc") { ActionCount = 512 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        ServiceRunnerTestWait.RunAndDrain(runner, clock, 512);
        var measuredBefore = runner.Snapshot.MeasuredWorkerCycleCount;
        definition.MeasureAppendAllocations = true;
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.MeasuredWorkerCycleCount > measuredBefore,
            ServiceCycleTestDeadline.Value));
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(0, definition.LastAppendAllocatedBytes);
        Assert.Equal(0, runner.Snapshot.WorkerCycleAllocatedBytes);

        var beforeDrain = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 512; index++)
            runner.TryExecuteOne(clock.Now);
        var drainAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeDrain;
        Assert.Equal(0, drainAllocated);

        var beforeIdle = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
            runner.TryAcquireResponse();
        var idleAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeIdle;
        Assert.Equal(0, idleAllocated);
    }
}
