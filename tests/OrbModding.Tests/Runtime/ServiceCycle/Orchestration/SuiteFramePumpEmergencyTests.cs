using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpEmergencyTests
{
    [Fact]
    public void EmergencyBeforeResponseRejectsLateBatchWithoutNativeCall()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.late") { ActionCount = 3 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        pump.SetEmergencyStop(true);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        var stopped = pump.PumpFrame(frame++);
        while (stopped.ResponsesAcquired == 0)
            stopped = pump.PumpFrame(frame++);

        Assert.Equal(1, stopped.ResponsesAcquired);
        Assert.Equal(1, stopped.EmergencyBatchesRejected);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, registration.Runner.Snapshot.PreviousReceipt.ResultCode);
        Assert.Equal(0, registration.Runner.Snapshot.PreviousReceipt.NativeCallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void EmergencyMidDrainPreservesCommittedPrefixAndClearDoesNotResumeBatch()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.mid") { ActionCount = 3 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PrepareBatch(pump, registration);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        pump.SetEmergencyStop(true);
        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(1, receipt.CommittedCount);
        Assert.Equal(2, receipt.ActionCount - receipt.CommittedCount);
        Assert.Equal(1, receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(1, definition.ActionExecutionCount);

        pump.SetEmergencyStop(false);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        var next = pump.PumpFrame(frame);
        Assert.Equal(0, next.ActionsAttempted);
        Assert.Equal(1, next.CapturesAttempted);
        Assert.Equal(1, definition.ActionExecutionCount);
    }

    [Fact]
    public void ReentrantEmergencyFinishesCurrentActionAndPreventsEveryLaterNativeCallback()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var first = new ExecutionServiceDefinition("pump.emergency.reentrant.first") { ActionCount = 2 };
        var second = new ExecutionServiceDefinition("pump.emergency.reentrant.second") { ActionCount = 2 };
        using var firstRegistration = registry.Register(first, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var secondRegistration = registry.Register(second, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        first.ActionCount = 0;
        second.ActionCount = 0;
        PrimeWorkerState(firstRegistration.Runner, clock);
        PrimeWorkerState(secondRegistration.Runner, clock);
        first.ActionCount = 2;
        second.ActionCount = 2;
        using var pump = new SuiteFramePump(registry);
        first.ActionCallback = () => pump.SetEmergencyStop(true);

        var frame = 1L;
        pump.PumpFrame(frame++);
        while (firstRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               secondRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForPhase(firstRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(secondRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        pump.PumpFrame(frame++);
        var action = pump.PumpFrame(frame);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(2, action.EmergencyBatchesRejected);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(0, second.ActionExecutionCount);
        Assert.Equal(1, firstRegistration.Runner.Snapshot.PreviousReceipt.CommittedCount);
        Assert.Equal(0, secondRegistration.Runner.Snapshot.PreviousReceipt.CommittedCount);
    }

    private static void PrimeWorkerState(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        Assert.True(runner.TryStartCycle(clock.Now).CaptureAttempted);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.False(runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void ReentrantEmergencyDuringCapturePreventsLaterCaptureCallbacks()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var first = new ExecutionServiceDefinition("pump.emergency.capture.first") { ActionCount = 1 };
        var second = new ExecutionServiceDefinition("pump.emergency.capture.second") { ActionCount = 1 };
        using var firstRegistration = registry.Register(first, new ExecutionConfig(1), new LifecycleGeneration(1));
        using var secondRegistration = registry.Register(second, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        first.CaptureCallback = () => pump.SetEmergencyStop(true);

        var capture = pump.PumpFrame(1);
        Assert.Equal(1, capture.CapturesAttempted);
        Assert.Equal(1, first.CaptureCount);
        Assert.Equal(0, second.CaptureCount);
        Assert.Equal(0, first.ActionExecutionCount);
        Assert.Equal(0, second.ActionExecutionCount);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HundredThousandReferenceActionsRejectInConstantUnityWorkAndClearOnWorker()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.huge") { ActionCount = 100_000 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        PrepareBatch(pump, registration);
        pump.SetEmergencyStop(true);
        var beforeCleanup = registration.Runner.Snapshot;
        Assert.Equal(1, beforeCleanup.Handoff.CleanupRequestCount);
        Assert.Equal(99_999, beforeCleanup.PreviousReceipt.UntouchedSuffixCount);
        Assert.Equal(0, definition.ActionExecutionCount);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        var afterCleanup = registration.Runner.Snapshot;
        Assert.Equal(1, afterCleanup.Handoff.CleanupAcknowledgementCount);
        Assert.Equal(afterCleanup.WorkerThreadId, afterCleanup.LastCleanupThreadId);
    }

    [Fact]
    public void EmergencyDoesNotRewritePinnedConfigurationAndFreshCycleUsesLatestSave()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.config") { ActionCount = 2 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        registration.Configuration.CompleteSave(ConfigurationSaveResult<ExecutionConfig>.Saved(new ExecutionConfig(2)));
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        while (pump.PumpFrame(frame++).ResponsesAcquired == 0) { }
        pump.PumpFrame(frame++);
        Assert.Equal(1, definition.LastExecutionConfig);
        pump.SetEmergencyStop(true);
        pump.SetEmergencyStop(false);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        while (pump.PumpFrame(frame++).ResponsesAcquired == 0) { }
        pump.PumpFrame(frame);
        Assert.Equal(2, definition.LastExecutionConfig);
    }

    private static long PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> registration)
    {
        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        while (pump.PumpFrame(frame++).ResponsesAcquired == 0) { }
        return frame;
    }
}
