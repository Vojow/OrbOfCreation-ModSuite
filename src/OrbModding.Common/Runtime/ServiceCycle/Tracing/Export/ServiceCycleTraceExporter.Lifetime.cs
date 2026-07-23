using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

public sealed partial class ServiceCycleTraceExporter
{
    /// <summary>
    /// Closes admission and signals the background worker to flush already accepted snapshots. This
    /// method never joins or waits for storage.
    /// </summary>
    public void Stop()
    {
        EnsureOwner();
        if (!TryBeginOwnerOperation()) return;
        var status = ReadStatus();
        if (status is not (ServiceCycleTraceExportStatus.Initializing or ServiceCycleTraceExportStatus.Running))
        {
            EndOwnerOperation();
            return;
        }
        if (Interlocked.CompareExchange(
                ref _status,
                (int)ServiceCycleTraceExportStatus.Stopping,
                (int)status) != (int)status)
        {
            EndOwnerOperation();
            return;
        }

        // Keep admission raised through the signal so cleanup cannot reclaim the wake handle between
        // the lifecycle transition and Set().
        try { _wake!.Set(); }
        finally { EndOwnerOperation(); }
    }

    /// <summary>Equivalent to <see cref="Stop"/>; disposal is intentionally signal-only.</summary>
    public void Dispose() => Stop();

    private bool TryBeginOwnerOperation()
    {
        if (Volatile.Read(ref _admissionClosed) != 0) return false;
        if (Interlocked.CompareExchange(ref _ownerOperationActive, 1, 0) != 0) return false;
        if (Volatile.Read(ref _admissionClosed) == 0) return true;
        Volatile.Write(ref _ownerOperationActive, 0);
        return false;
    }

    private void EndOwnerOperation() => Volatile.Write(ref _ownerOperationActive, 0);

    private void CloseAdmissionAndWait()
    {
        Volatile.Write(ref _admissionClosed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _ownerOperationActive) != 0)
        {
            spinner.SpinOnce();
        }
    }

    private void LatchSourceFault()
    {
        Interlocked.Exchange(ref _status, (int)ServiceCycleTraceExportStatus.Faulted);
        Interlocked.CompareExchange(ref _faultCount, 1, 0);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Semantic snapshot export is owner-thread affine.");
    }
}
