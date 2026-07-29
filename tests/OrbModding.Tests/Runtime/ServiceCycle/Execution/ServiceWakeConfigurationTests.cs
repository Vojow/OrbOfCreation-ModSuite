using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceWakeConfigurationTests
{
    [Fact]
    public void PublishingConfigurationInvalidatesAStartWaitFromThePreviousGeneration()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.config-wakes-start")
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(1000))),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.False(runner.TryStartCycle(clock.Now).Queued);
        Assert.Equal(1100, runner.Snapshot.NextWakeDue.Ticks);
        definition.StartDecision =
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        Assert.False(runner.TryStartCycle(clock.Now).Queued);
        Assert.Equal(1, definition.StartCount);

        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

        var awakened = runner.TryStartCycle(clock.Now);

        Assert.True(awakened.Queued);
        Assert.Equal(2, definition.StartCount);
        Assert.Equal(2UL, awakened.Cycle.Config.Value);
    }

    [Fact]
    public void ConfigurationPublishedDuringEvaluationInvalidatesItsResultingWake()
    {
        var clock = new ThreadSafeTestClock(100);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.config-wakes-result")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
            EvaluationWake =
                WakePolicy.AfterDecision(new MonotonicDuration(1000)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        var first = runner.TryStartCycle(clock.Now);
        Assert.True(first.Queued);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        release.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(1100, runner.Snapshot.NextWakeDue.Ticks);

        var awakened = runner.TryStartCycle(clock.Now);

        Assert.True(awakened.Queued);
        Assert.Equal(1UL, first.Cycle.Config.Value);
        Assert.Equal(2UL, awakened.Cycle.Config.Value);
    }

    [Fact]
    public void PublishingConfigurationDoesNotBypassStartFaultBackoff()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.config-keeps-fault-backoff")
        {
            ShouldStartCallback = () =>
                throw new InvalidOperationException("synthetic start fault"),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.False(runner.TryStartCycle(clock.Now).Queued);
        Assert.Equal(110, runner.Snapshot.NextWakeDue.Ticks);

        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

        Assert.False(runner.TryStartCycle(clock.Now).Queued);
        Assert.Equal(1, definition.StartCount);
        Assert.Equal(110, runner.Snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void HandoffIsNonblockingAndConfigurationIsPinnedForOneCycle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.handoff")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(1));
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        Assert.True(SpinWait.SpinUntil(
            () => runner.Snapshot.WorkerThreadId != 0 && runner.Snapshot.Handoff.WorkerWaitCount != 0,
            ServiceCycleTestDeadline.Value));
        Assert.Equal(ServiceHandoffPhase.Empty, runner.Snapshot.Handoff.Phase);
        Assert.Equal(1, runner.Snapshot.Handoff.WorkerWaitCount);
        Assert.True(runner.Snapshot.WorkerIsBackground);
        Assert.Equal("Orb.ServiceCycle.test.execution.handoff.lifecycle-1", runner.WorkerName);

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        Assert.Equal(ServiceHandoffPhase.Evaluating, runner.ProbeHandoff().Phase);

        var stopwatch = Stopwatch.StartNew();
        Assert.False(runner.TryAcquireResponse());
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        release.Set();

        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(ServiceHandoffPhase.Empty, runner.Snapshot.Handoff.Phase);
        Assert.Equal(1, definition.LastEvaluatedSetting);
        Assert.Equal(3UL, runner.Snapshot.Projection.LatestConfiguration.Value);
        Assert.Equal(1L, runner.Snapshot.Projection.Snapshot.GetEntry(0).Value.Integer);

        definition.EvaluationRelease = null;
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.Equal(2, definition.LastEvaluatedSetting);
        Assert.Equal(2L, runner.Snapshot.Projection.Snapshot.GetEntry(0).Value.Integer);
    }

    [Theory]
    [InlineData(WakePolicyKind.AfterDecision, false, 110)]
    [InlineData(WakePolicyKind.AfterBatch, false, 130)]
    [InlineData(WakePolicyKind.AfterBatch, true, 110)]
    public void WakeAnchorsDistinguishDecisionBatchAndZeroActionTerminal(
        WakePolicyKind kind,
        bool zeroActions,
        long expectedDue)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"test.execution.wake.{kind}.{zeroActions}")
        {
            ActionCount = zeroActions ? 0 : 1,
            EvaluationWake = kind == WakePolicyKind.AfterDecision
                ? WakePolicy.AfterDecision(new MonotonicDuration(10))
                : WakePolicy.AfterBatch(new MonotonicDuration(10)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        clock.AdvanceTo(new MonotonicTimestamp(120));
        Assert.True(runner.TryAcquireResponse());
        if (!zeroActions) Assert.True(runner.TryExecuteOne(clock.Now).BatchTerminal);
        Assert.Equal(expectedDue, runner.Snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void AfterBatchDelayBeginsWhenTheTerminalActionCallbackEnds()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.wake.action-end")
        {
            ActionCount = 1,
            EvaluationWake = WakePolicy.AfterBatch(new MonotonicDuration(10)),
            ActionCallback = () => clock.Advance(new MonotonicDuration(13)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var attemptedAt = clock.Now;

        var dispatch = runner.TryExecuteOne(attemptedAt);

        Assert.True(dispatch.BatchTerminal);
        Assert.Equal(attemptedAt, definition.LastActionAttemptedAt);
        Assert.Equal(113, dispatch.Receipt.CompletedAt.Ticks);
        Assert.Equal(123, runner.Snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void StartFaultBackoffBeginsWhenTheShouldStartCallbackEnds()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.start-fault-end");
        definition.ShouldStartCallback = () =>
        {
            clock.Advance(new MonotonicDuration(13));
            throw new InvalidOperationException("synthetic start fault");
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        var attempt = runner.TryStartCycle(clock.Now);
        var snapshot = runner.Snapshot;

        Assert.False(attempt.Queued);
        Assert.False(attempt.StartDecisionFact.IsPresent);
        Assert.Equal(ServiceFaultCategory.Start, snapshot.Fault.Category);
        Assert.Equal(113, snapshot.Fault.ObservedAt.Ticks);
        Assert.Equal(123, snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void ActionFaultReceiptAndBackoffUseTheCallbackEndTimestamp()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.action-fault-end")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
            ActionCallback = () => clock.Advance(new MonotonicDuration(13)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        var attemptedAt = clock.Now;

        var dispatch = runner.TryExecuteOne(attemptedAt);
        var snapshot = runner.Snapshot;

        Assert.True(dispatch.BatchTerminal);
        Assert.Equal(attemptedAt, definition.LastActionAttemptedAt);
        Assert.Equal(BatchTerminalDisposition.Faulted, dispatch.Receipt.Disposition);
        Assert.Equal(113, dispatch.Receipt.CompletedAt.Ticks);
        Assert.Equal(ServiceFaultCategory.ActionExecution, snapshot.Fault.Category);
        Assert.Equal(113, snapshot.Fault.ObservedAt.Ticks);
        Assert.Equal(123, snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void StartWaitDelayBeginsWhenTheDecisionCallbackEnds()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.start-wait-end")
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(10))),
            ShouldStartCallback = () => clock.Advance(new MonotonicDuration(13)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));

        var attempt = registration.Runner.TryStartCycle(clock.Now);

        Assert.False(attempt.Queued);
        Assert.False(attempt.CaptureAttempted);
        Assert.Equal(123, registration.Runner.Snapshot.NextWakeDue.Ticks);
    }

    [Fact]
    public void CaptureUnavailableDelayBeginsWhenTheCaptureCallbackEnds()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("test.source.capture-wait-end")
        {
            CaptureResult = ServiceCaptureResult.Unavailable(
                CommonServiceDecisionCodes.CaptureUnavailable,
                WakePolicy.AfterDecision(new MonotonicDuration(10))),
            CaptureCallback = () => clock.Advance(new MonotonicDuration(13)),
        };
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));

        var attempt = registration.Runner.TryStartCycle(clock.Now);

        Assert.False(attempt.Queued);
        Assert.True(attempt.CaptureAttempted);
        Assert.Equal(123, registration.Runner.Snapshot.NextWakeDue.Ticks);
    }
}
