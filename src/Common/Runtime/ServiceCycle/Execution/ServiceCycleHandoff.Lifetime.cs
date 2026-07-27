using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff
{
    internal void SignalStop()
    {
        Volatile.Write(ref _stopRequested, true);
        if (Volatile.Read(ref _responseWaiterCount) != 0 ||
            Volatile.Read(ref _workerReadyWaiterCount) != 0)
        {
            lock (_gate)
            {
                PulseOfflineWaitersUnderGate();
            }
        }
        try
        {
            _workerWake.Set();
        }
        catch (ObjectDisposedException) when (WorkerWakeDisposed)
        {
        }
    }

    internal void PrepareWorkerExit()
    {
        lock (_gate)
        {
            _request = default;
            _response = default;
            _cleanupPending = false;
            _cleanupClaimed = false;
            _cleanupFrom = 0;
            _cleanupCount = 0;
            if (_phase != ServiceHandoffPhase.Stopping)
                TransitionTo(ServiceHandoffPhase.Stopping);
            PulseOfflineWaitersUnderGate();
        }
        DisposeWorkerWake();
        Volatile.Write(ref _workerExitPrepared, 1);
    }

    internal bool TryAcknowledgeWorkerExited()
    {
        if (!WorkerExitPrepared || !WorkerWakeDisposed) return false;
        lock (_gate)
        {
            if (_phase == ServiceHandoffPhase.Stopped) return true;
            if (_phase != ServiceHandoffPhase.Stopping) return false;
            TransitionTo(ServiceHandoffPhase.Stopped);
            PulseOfflineWaitersUnderGate();
            return true;
        }
    }

    internal void DisposeNeverStarted() => DisposeWorkerWake();

    private void DisposeWorkerWake()
    {
        if (Interlocked.Exchange(ref _workerWakeDisposed, 1) == 0)
            _workerWake.Dispose();
    }
}
