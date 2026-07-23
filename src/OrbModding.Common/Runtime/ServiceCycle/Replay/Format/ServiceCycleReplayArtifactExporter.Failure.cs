using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public sealed partial class ServiceCycleReplayArtifactExporter
{
    private void LatchFault(
        ServiceCycleReplayExportSlot? failed,
        ServiceCycleReplayExporterFaultReason reason)
    {
        Interlocked.Exchange(ref _status, (int)ServiceCycleReplayExportStatus.Faulted);
        var firstFault = Interlocked.CompareExchange(ref _faults, 1, 0) == 0;
        CloseAdmissionAndWait();
        ReleaseStagedSnapshot();
        if (failed is not null)
            DiscardAccepted(failed, ServiceCycleReplayArtifactDiscardReason.WriteFailed);
        if (_first is { } first)
            DiscardReady(first, ServiceCycleReplayArtifactDiscardReason.ExporterFaulted);
        if (_second is { } second)
            DiscardReady(second, ServiceCycleReplayArtifactDiscardReason.ExporterFaulted);
        if (firstFault) NotifyFaulted(reason);
    }

    private void LatchOwnerFault(ServiceCycleReplayExporterFaultReason reason)
    {
        Interlocked.Exchange(ref _status, (int)ServiceCycleReplayExportStatus.Faulted);
        if (Interlocked.CompareExchange(ref _faults, 1, 0) == 0) NotifyFaulted(reason);
    }

    private void ReleaseStagedSnapshot() => _stager.Release();

    private void DiscardReady(
        ServiceCycleReplayExportSlot slot,
        ServiceCycleReplayArtifactDiscardReason reason)
    {
        var ordinal = slot.Ordinal;
        if (!ServiceCycleReplayExportSlotPool.TryDiscardReady(slot)) return;
        Interlocked.Increment(ref _discarded);
        Interlocked.Decrement(ref _pending);
        NotifyDiscarded(ordinal, reason);
    }

    private void DiscardAccepted(
        ServiceCycleReplayExportSlot slot,
        ServiceCycleReplayArtifactDiscardReason reason)
    {
        var ordinal = slot.Ordinal;
        Interlocked.Increment(ref _discarded);
        Interlocked.Decrement(ref _pending);
        ServiceCycleReplayExportSlotPool.Release(slot);
        NotifyDiscarded(ordinal, reason);
    }

    private void NotifyCommitted(int ordinal, int bytes)
    {
        try
        {
            _observer?.ArtifactCommitted(ordinal, bytes);
        }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception))
        {
        }
    }

    private void NotifyDiscarded(
        int ordinal,
        ServiceCycleReplayArtifactDiscardReason reason)
    {
        try
        {
            _observer?.ArtifactDiscarded(ordinal, reason);
        }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception))
        {
        }
    }

    private void NotifyFaulted(ServiceCycleReplayExporterFaultReason reason)
    {
        try
        {
            _observer?.ExporterFaulted(reason);
        }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception))
        {
        }
    }
}
