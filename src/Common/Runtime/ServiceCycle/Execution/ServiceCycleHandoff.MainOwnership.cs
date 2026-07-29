using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff
{
    internal void CompleteMainOwnership()
    {
        lock (_gate)
        {
            if (_phase != ServiceHandoffPhase.MainOwnedBatch)
                throw new InvalidOperationException(
                    "Only a main-owned batch can return the handoff to Empty.");
            _response = default;
            TransitionTo(ServiceHandoffPhase.Empty);
        }
    }

    internal bool TryCompleteMainOwnershipNonBlocking()
    {
        if (PhaseHint != ServiceHandoffPhase.MainOwnedBatch ||
            !Monitor.TryEnter(_gate, 0))
            return false;
        try
        {
            if (_phase != ServiceHandoffPhase.MainOwnedBatch) return false;
            _response = default;
            TransitionTo(ServiceHandoffPhase.Empty);
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    internal void CompleteMainOwnershipWithWorkerCleanup(
        int clearFrom,
        int clearCount)
    {
        ValidateCleanupRange(clearFrom, clearCount);
        lock (_gate)
        {
            if (_phase != ServiceHandoffPhase.MainOwnedBatch || _cleanupPending)
                throw new InvalidOperationException(
                    "The handoff cannot accept suffix cleanup.");
            ScheduleCleanupUnderGate(clearFrom, clearCount);
        }
    }

    internal bool TryCompleteMainOwnershipWithWorkerCleanupNonBlocking(
        int clearFrom,
        int clearCount)
    {
        ValidateCleanupRange(clearFrom, clearCount);
        if (PhaseHint != ServiceHandoffPhase.MainOwnedBatch ||
            !Monitor.TryEnter(_gate, 0))
            return false;
        try
        {
            if (_phase != ServiceHandoffPhase.MainOwnedBatch || _cleanupPending)
                return false;
            ScheduleCleanupUnderGate(clearFrom, clearCount);
            return true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    internal void AcknowledgeCleanup(int workerThreadId)
    {
        if (workerThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerThreadId));
        lock (_gate)
        {
            if (!_cleanupPending || !_cleanupClaimed)
                throw new InvalidOperationException(
                    "No worker cleanup is in progress.");
            _cleanupPending = false;
            _cleanupAcknowledgementCount++;
            _lastCleanupThreadId = workerThreadId;
            _cleanupClaimed = false;
            _cleanupFrom = 0;
            _cleanupCount = 0;
        }
    }

    private void ScheduleCleanupUnderGate(int clearFrom, int clearCount)
    {
        _cleanupFrom = clearFrom;
        _cleanupCount = clearCount;
        _cleanupPending = true;
        _cleanupRequestCount++;
        _cleanupClaimed = false;
        _response = default;
        TransitionTo(ServiceHandoffPhase.Empty);
        _workerWake.Set();
    }

    private static void ValidateCleanupRange(int clearFrom, int clearCount)
    {
        if (clearFrom < 0 || clearCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(clearCount));
    }
}
