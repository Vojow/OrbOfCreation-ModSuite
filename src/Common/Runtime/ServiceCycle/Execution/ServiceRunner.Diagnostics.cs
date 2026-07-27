using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

public sealed partial class ServiceRunner<TState, TAction>
{
    internal ServiceRunnerSnapshot Snapshot
    {
        get
        {
            AssertOwnerThread();
            _worker.TryAcknowledgeExit();
            return _diagnostics.Read(_disposed);
        }
    }

    internal bool TrySnapshot(out ServiceRunnerSnapshot snapshot)
    {
        AssertOwnerThread();
        return _diagnostics.TryRead(_disposed, out snapshot);
    }

    internal ServiceHandoffPhase HandoffPhaseHint
    {
        get
        {
            AssertOwnerThread();
            _worker.TryAcknowledgeExit();
            return _handoff.PhaseHint;
        }
    }

    internal bool WaitForResponseReady(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        AssertOwnerThread();
        if (_disposed) return false;
        if (expectedCycle.Service != _serviceId ||
            expectedCycle.Lifecycle != Lifecycle)
            return false;
        return _handoff.WaitForResponseReady(expectedCycle, timeout);
    }

    internal bool WaitForWorkerReady(TimeSpan timeout)
    {
        AssertOwnerThread();
        if (_disposed) return false;
        return _handoff.WaitForWorkerReady(timeout);
    }

    internal bool WaitForResponseReadyAndWorkerSettled(
        ServiceCycleIdentity expectedCycle,
        TimeSpan timeout)
    {
        AssertOwnerThread();
        if (_disposed) return false;
        if (expectedCycle.Service != _serviceId ||
            expectedCycle.Lifecycle != Lifecycle)
            return false;
        return _handoff.WaitForResponseReadyAndWorkerSettled(
            expectedCycle,
            timeout);
    }

    internal bool WaitForResponseReady(TimeSpan timeout)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        return _handoff.WaitForResponseReady(timeout);
    }

    internal bool WaitForWorkerExit(TimeSpan timeout)
    {
        AssertOwnerThread();
        if (timeout < TimeSpan.Zero ||
            timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "A finite bounded timeout is required.");
        }
        return _worker.WaitForThreadExit(timeout);
    }

    internal ServiceHandoffPhase DiagnosticsHandoffPhaseHint
    {
        get
        {
            AssertOwnerThread();
            return _diagnostics.HandoffPhaseHint;
        }
    }

    internal ServiceRunnerStorageSnapshot ReadStorageNonBlocking()
    {
        AssertOwnerThread();
        return _diagnostics.ReadStorageNonBlocking();
    }

    internal bool WorkerExitPrepared => _handoff.WorkerExitPrepared;
    internal bool WorkerWakeDisposed => _handoff.WorkerWakeDisposed;

    internal bool TryAcknowledgeWorkerExit()
    {
        AssertOwnerThread();
        return _worker.TryAcknowledgeExit();
    }

    internal ServiceHandoffSnapshot ProbeHandoff()
    {
        AssertOwnerThread();
        _worker.TryAcknowledgeExit();
        return _handoff.Snapshot;
    }
}
