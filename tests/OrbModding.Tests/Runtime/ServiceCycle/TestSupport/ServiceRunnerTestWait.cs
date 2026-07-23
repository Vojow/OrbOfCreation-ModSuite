using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceRunnerTestWait
{
    internal static void ForPhase<TFrame, TConfig, TState, TAction>(
        ServiceRunner<TFrame, TConfig, TState, TAction> runner,
        ServiceHandoffPhase phase)
        where TConfig : notnull
    {
        if (!SpinWait.SpinUntil(() => runner.Snapshot.Handoff.Phase == phase, TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"Runner did not reach phase {phase}; current {runner.Snapshot.Handoff.Phase}.");
    }

    internal static void ForCleanup(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner)
    {
        if (!SpinWait.SpinUntil(() => !runner.Snapshot.Handoff.CleanupPending, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Runner suffix cleanup did not complete.");
    }

    internal static ServiceHandoffSnapshot ForHandoff<TFrame, TConfig, TState, TAction>(
        ServiceRunner<TFrame, TConfig, TState, TAction> runner,
        Func<ServiceHandoffSnapshot, bool> predicate,
        string expectation)
        where TConfig : notnull
    {
        var observed = default(ServiceHandoffSnapshot);
        if (!SpinWait.SpinUntil(
                () => predicate(observed = runner.ProbeHandoff()),
                TimeSpan.FromSeconds(2)))
        {
            throw new TimeoutException(
                $"Runner did not reach {expectation}; current phase {observed.Phase}, " +
                $"waits {observed.WorkerWaitCount}, cleanup acknowledgements " +
                $"{observed.CleanupAcknowledgementCount}.");
        }
        return observed;
    }

    internal static long PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> registration)
    {
        var frame = 1L;
        var waiting = ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.Empty && value.WorkerWaitCount > 0,
            "the initial worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).CapturesAttempted);
        ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.ResponseReady &&
                value.WorkerWaitCount > waiting.WorkerWaitCount,
            "the prepared response and worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).ResponsesAcquired);
        return frame;
    }

    internal static void PublishDeferredRequest<TFrame, TConfig, TState, TAction>(
        SuiteFramePump pump,
        ServiceRunner<TFrame, TConfig, TState, TAction> runner,
        ref long frameIdentity)
        where TConfig : notnull
    {
        while (runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            pump.PumpFrame(frameIdentity++);
    }

    internal static void RunZeroActionCycle(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
    }

    internal static void RunAndDrain(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock,
        int count)
    {
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        for (var index = 0; index < count; index++)
            Assert.True(runner.TryExecuteOne(clock.Now).Attempted);
    }

    internal static void RunRejectedBatch(
        ServiceRunner<ExecutionFrame, ExecutionConfig, ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.True(runner.TryExecuteOne(clock.Now).BatchTerminal);
    }
}
