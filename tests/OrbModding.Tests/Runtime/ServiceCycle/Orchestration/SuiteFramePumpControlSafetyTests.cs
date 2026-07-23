using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpControlSafetyTests
{
    [Fact]
    public void ResponseReadyDuringEmergencyEpisodeRemainsRejectedAfterImmediateClear()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.episode.response") { ActionCount = 3 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);

        pump.SetEmergencyStop(true);
        var engagedGeneration = pump.EmergencyTransition;
        pump.SetEmergencyStop(false);
        clock.Advance(new MonotonicDuration(17));
        while (pump.PumpFrame(frame++).ResponsesAcquired == 0) { }

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(1, engagedGeneration.Value);
        Assert.Equal(2, pump.EmergencyTransition.Value);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(clock.Now, receipt.CompletedAt);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.True(registration.Runner.Snapshot.Projection.IsPresent);
    }

    [Fact]
    public void EvaluationBlockedDuringEmergencyEpisodeRemainsRejectedAfterImmediateClear()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.episode.evaluation")
        {
            ActionCount = 2,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        pump.SetEmergencyStop(true);
        pump.SetEmergencyStop(false);
        release.Set();
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        clock.Advance(new MonotonicDuration(23));
        while (pump.PumpFrame(frame++).ResponsesAcquired == 0) { }

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(clock.Now, receipt.CompletedAt);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(1, definition.EvaluationCount);
    }

    [Fact]
    public void RegistrationDisposeDuringCaptureIsRejectedWithoutLosingHandle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.guard.registration") { ActionCount = 0 };
        var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        Exception? observed = null;
        definition.CaptureCallback = () =>
        {
            try { registration.Dispose(); }
            catch (Exception ex) { observed = ex; }
        };
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var report = pump.PumpFrame(1);

        Assert.Equal(1, report.CapturesAttempted);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, registration.Configuration.ReadLatest().Snapshot.Value);
        definition.CaptureCallback = null;
        registration.Dispose();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RegistryDisposeDuringActionIsRejectedUntilCallbackReturns()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.guard.registry") { ActionCount = 1 };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        Exception? observed = null;
        definition.ActionCallback = () =>
        {
            try { registry.Dispose(); }
            catch (Exception ex) { observed = ex; }
        };
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = PrepareBatch(pump, registration);

        var action = pump.PumpFrame(frame);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, definition.ActionExecutionCount);
        Assert.Equal(1, registration.Configuration.ReadLatest().Snapshot.Value);
    }

    [Fact]
    public void ThrowingCaptureIsCountedAndTimedWithoutPublishingWork()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.capture.throw") { ActionCount = 1 };
        definition.CaptureCallback = () =>
        {
            clock.Advance(new MonotonicDuration(13));
            throw new InvalidOperationException("synthetic capture fault");
        };
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        const int maximumCaptureFrames = 32;
        var report = default(SuiteFramePumpReport);
        var captureFrame = 0L;
        var capturesAttempted = 0;
        for (var frame = 1L; frame <= maximumCaptureFrames; frame++)
        {
            var candidate = pump.PumpFrame(frame);
            capturesAttempted += candidate.CapturesAttempted;
            if (candidate.CapturesAttempted != 0)
            {
                report = candidate;
                captureFrame = frame;
                break;
            }
            Thread.Yield();
        }

        Assert.NotEqual(0, captureFrame);
        var afterCapture = pump.PumpFrame(captureFrame + 1);
        capturesAttempted += afterCapture.CapturesAttempted;
        var duplicate = pump.PumpFrame(captureFrame);

        Assert.Equal(1, report.CapturesAttempted);
        Assert.Equal(1, capturesAttempted);
        Assert.Equal(13, report.CaptureDuration.Ticks);
        Assert.True(report.TotalDuration.Ticks >= 13);
        Assert.True(afterCapture.Accepted);
        Assert.Equal(0, afterCapture.CapturesAttempted);
        Assert.Equal(1, definition.CaptureCount);
        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        Assert.False(duplicate.Accepted);
        Assert.Equal(1, definition.CaptureCount);
    }

    [Fact]
    public void ThrowingActionMapsToAdapterFaultAndAbortsTheUntouchedSuffix()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.action.throw") { ActionCount = 3 };
        definition.ActionCallback = () => throw new InvalidOperationException("synthetic action fault");
        using var registration = registry.Register(definition, new ExecutionConfig(1), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = PrepareBatch(pump, registration);

        var action = pump.PumpFrame(frame++);
        var receipt = registration.Runner.Snapshot.PreviousReceipt;

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(1, definition.ActionExecutionCount);
        Assert.Equal(BatchTerminalDisposition.Faulted, receipt.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, receipt.ResultCode);
        Assert.Equal(0, receipt.CommittedCount);
        Assert.Equal(2, receipt.UntouchedSuffixCount);
        Assert.Equal(0, receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(ServiceFaultCategory.ActionExecution, registration.Runner.Snapshot.Fault.Category);

        var handback = pump.PumpFrame(frame);
        Assert.Equal(0, handback.ActionsAttempted);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        Assert.Equal(1, definition.ActionExecutionCount);
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
