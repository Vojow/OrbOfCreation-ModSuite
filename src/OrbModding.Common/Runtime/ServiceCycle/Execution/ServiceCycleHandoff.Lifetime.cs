using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleHandoff<TConfig>
    where TConfig : notnull
{
    internal void ThrowIfWorkerFatal() =>
        Volatile.Read(ref _workerFatalException)?.Throw();

    internal void PublishWorkerFatal(ExceptionDispatchInfo fatal)
    {
        if (fatal is null) throw new ArgumentNullException(nameof(fatal));
        lock (_gate)
        {
            if (_workerFatalException is null)
                Volatile.Write(ref _workerFatalException, fatal);
            PulseOfflineWaitersUnderGate();
            Monitor.PulseAll(_gate);
        }
    }

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

    internal void PrepareWorkerExit(ExceptionDispatchInfo? fatal = null)
    {
        lock (_gate)
        {
            if (fatal is not null && _workerFatalException is null)
                Volatile.Write(ref _workerFatalException, fatal);
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
