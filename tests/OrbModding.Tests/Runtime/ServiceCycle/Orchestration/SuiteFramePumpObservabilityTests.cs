using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpObservabilityTests
{
    [Fact]
    public void CumulativeLifecycleTransitionsIncludeInitialInstallAndBetweenFrameReplacement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("pump.observability.lifecycle"),
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var initial = ServiceCycleDiagnostics.ReadPump(pump);

        Assert.Equal(1, initial.LifecyclePositionTransitions);
        Assert.Equal(0, initial.AcceptedFrameCount);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));

        var replaced = ServiceCycleDiagnostics.ReadPump(pump);

        Assert.Equal(3, replaced.LifecyclePositionTransitions);
        Assert.Equal(0, replaced.AcceptedFrameCount);
        Assert.Equal((ulong)2, replaced.CurrentLifecycle.Value);
    }

    [Fact]
    public void CumulativeEmergencyRejectionsIncludeActiveBatchTerminatedBetweenFramesExactlyOnce()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.observability.emergency")
        {
            ActionCount = 3,
        };
        using var registration = registry.Register(
            definition,
            new ExecutionConfig(1),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);

        var before = ServiceCycleDiagnostics.ReadPump(pump);
        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        var afterImmediateRejection = ServiceCycleDiagnostics.ReadPump(pump);
        pump.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
        var afterIdempotentSweep = ServiceCycleDiagnostics.ReadPump(pump);
        var nextFrame = pump.PumpFrame(frame);
        var afterNextFrame = ServiceCycleDiagnostics.ReadPump(pump);

        Assert.Equal(0, before.EmergencyBatchesRejected);
        Assert.Equal(1, afterImmediateRejection.EmergencyBatchesRejected);
        Assert.Equal(1, afterIdempotentSweep.EmergencyBatchesRejected);
        Assert.Equal(0, nextFrame.EmergencyBatchesRejected);
        Assert.Equal(1, afterNextFrame.EmergencyBatchesRejected);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(BatchTerminalDisposition.Rejected, registration.Runner.Snapshot.PreviousReceipt.Disposition);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, registration.Runner.Snapshot.PreviousReceipt.ResultCode);
        Assert.Equal(
            EmergencyStopReason.SafetyInterlock,
            registration.Runner.Snapshot.PreviousReceipt.EmergencyStop.Reason);
    }
}
