using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

public sealed partial class ServiceCycleTraceExporter
{
    private void WorkerLoop()
    {
        try
        {
            var recovery = _storage.Reconcile(_maximumCommittedSnapshots);
            _nextOrdinal = recovery.NextOrdinal;
            Volatile.Write(ref _retainedSnapshots, recovery.RetainedSegments);
            Volatile.Write(ref _startupPrunedSnapshots, recovery.StartupPrunedSegments);
            Volatile.Write(ref _staleTemporaryFilesRemoved, recovery.StaleTemporaryFilesRemoved);
            Interlocked.CompareExchange(
                ref _status,
                (int)ServiceCycleTraceExportStatus.Running,
                (int)ServiceCycleTraceExportStatus.Initializing);

            while (true)
            {
                var slot = TryTakeNextReady();
                if (slot is not null)
                {
                    if (!Export(slot)) return;
                    continue;
                }

                var status = ReadStatus();
                if (status == ServiceCycleTraceExportStatus.Running)
                {
                    _wake!.WaitOne();
                    continue;
                }

                if (status == ServiceCycleTraceExportStatus.Stopping)
                {
                    CloseAdmissionAndWait();

                    // Recheck after observing closed admission so a just-published slot cannot be missed.
                    if (TryTakeNextReady() is { } finalSlot)
                    {
                        if (!Export(finalSlot)) return;
                        continue;
                    }

                    Interlocked.CompareExchange(
                        ref _status,
                        (int)ServiceCycleTraceExportStatus.Stopped,
                        (int)ServiceCycleTraceExportStatus.Stopping);
                }

                return;
            }
        }
        catch
        {
            LatchFault(null);
        }
        finally
        {
            CloseAdmissionAndWait();
            _wake?.Dispose();
        }
    }

    private ServiceCycleTraceExportSlot? TryTakeNextReady()
    {
        var first = _first!;
        var second = _second!;
        var firstReady = Volatile.Read(ref first.State) == ServiceCycleTraceExportSlot.Ready;
        var secondReady = Volatile.Read(ref second.State) == ServiceCycleTraceExportSlot.Ready;
        if (!firstReady && !secondReady) return null;

        var candidate = firstReady && secondReady
            ? (first.Ordinal <= second.Ordinal ? first : second)
            : (firstReady ? first : second);
        return Interlocked.CompareExchange(
                ref candidate.State,
                ServiceCycleTraceExportSlot.WorkerOwned,
                ServiceCycleTraceExportSlot.Ready) == ServiceCycleTraceExportSlot.Ready
            ? candidate
            : null;
    }
}
