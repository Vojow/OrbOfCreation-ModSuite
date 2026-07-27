using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed class ServiceRunnerPosition<TState, TAction>
{
    private ServiceRunner<TState, TAction>? _runner;

    internal ServiceRunnerPosition(int index) => Index = index;

    internal int Index { get; }
    internal ServiceRunnerPositionState State { get; private set; }
    internal ServiceRunner<TState, TAction>? Runner => _runner;
    internal bool IsVacant => State == ServiceRunnerPositionState.Vacant;
    internal bool IsBetweenCycles => _runner is null || _runner.IsBetweenCycles;
    internal bool OwnsLifecycle(LifecycleGeneration lifecycle) =>
        _runner is not null && _runner.Lifecycle == lifecycle;

    /// <summary>
    /// Resolves an offline wait against this exact retained physical position. Current and retiring
    /// positions are both eligible; a recycled or different-lifecycle position fails closed.
    /// </summary>
    internal bool WaitForResponseReady(ServiceCycleIdentity expectedCycle, TimeSpan timeout)
    {
        var runner = _runner;
        return runner is not null && runner.Lifecycle == expectedCycle.Lifecycle &&
            runner.WaitForResponseReady(expectedCycle, timeout);
    }

    internal bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        var runner = _runner;
        return runner is not null && runner.Lifecycle == expectedCycle.Lifecycle &&
            runner.WaitForResponseReadyAndWorkerSettled(expectedCycle, timeout);
    }

    internal void InstallCurrent(ServiceRunner<TState, TAction> runner)
    {
        if (!IsVacant || _runner is not null)
            throw new InvalidOperationException("Only a vacant physical runner position can become current.");
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        State = ServiceRunnerPositionState.Current;
    }

    internal void MarkRetiring()
    {
        if (State != ServiceRunnerPositionState.Current || _runner is null)
            throw new InvalidOperationException("Only the current runner position can retire.");
        State = ServiceRunnerPositionState.Retiring;
    }

    internal bool TryReleaseStopped()
    {
        if (State != ServiceRunnerPositionState.Retiring || _runner is null)
            return false;
        _runner.TryAcknowledgeWorkerExit();
        if (_runner.HandoffPhaseHint != ServiceHandoffPhase.Stopped) return false;
        _runner = null;
        State = ServiceRunnerPositionState.Vacant;
        return true;
    }

    internal bool WaitForWorkerExit(TimeSpan timeout) =>
        _runner is null || _runner.WaitForWorkerExit(timeout);

    internal void SignalDispose()
    {
        _runner?.Dispose();
        if (_runner is not null) State = ServiceRunnerPositionState.Retiring;
    }

    internal ServiceRunnerPositionSnapshot Snapshot => new(
        Index,
        State,
        _runner?.Lifecycle ?? default,
        _runner?.DiagnosticsHandoffPhaseHint ?? ServiceHandoffPhase.Stopped,
        _runner?.ReadStorageNonBlocking() ?? default);
}
