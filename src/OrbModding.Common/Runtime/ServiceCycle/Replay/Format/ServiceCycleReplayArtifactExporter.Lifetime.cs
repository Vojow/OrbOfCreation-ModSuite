using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public sealed partial class ServiceCycleReplayArtifactExporter
{
    public void Stop()
    {
        EnsureOwner();
        if (!TryBeginOwnerOperation()) return;
        try
        {
            ReleaseStagedSnapshot();
            while (true)
            {
                var status = ReadStatus();
                if (status is not (
                    ServiceCycleReplayExportStatus.Initializing or
                    ServiceCycleReplayExportStatus.Running))
                    return;
                if (Interlocked.CompareExchange(
                        ref _status,
                        (int)ServiceCycleReplayExportStatus.Stopping,
                        (int)status) == (int)status)
                    break;
            }
            _wake!.Set();
        }
        finally
        {
            EndOwnerOperation();
        }
    }

    public void Dispose() => Stop();

    private void CloseAdmissionAndWait()
    {
        Volatile.Write(ref _admissionClosed, 1);
        var spinner = new SpinWait();
        while (Volatile.Read(ref _ownerOperationActive) != 0) spinner.SpinOnce();
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Replay artifact export is owner-thread affine.");
    }
}
