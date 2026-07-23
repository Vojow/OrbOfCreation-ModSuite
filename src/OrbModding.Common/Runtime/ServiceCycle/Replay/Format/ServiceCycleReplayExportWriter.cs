using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>Performs background-only encoding and restart-aware segment commit/pruning.</summary>
internal static class ServiceCycleReplayExportWriter
{
    internal static ServiceCycleReplayExportWriteResult Write(
        ServiceCycleReplaySession recording,
        IRestartAwareTraceSegmentStorage storage,
        ServiceCycleReplayExportSlot slot,
        int retained,
        int maximumCommitted)
    {
        object? segment = null;
        var committed = false;
        var encoded = 0;
        try
        {
            var snapshot = slot.Recording;
            var artifact = ServiceCycleReplayArtifactCodec.Encode(
                slot.Dropped,
                slot.Events.AsSpan(0, slot.EventCount),
                recording,
                in snapshot);
            encoded = artifact.Length;
            segment = storage.BeginSegment(slot.Ordinal);
            storage.Append(segment, artifact);
            storage.CommitSegment(segment);
            segment = null;
            committed = true;
            retained++;
            if (retained > maximumCommitted)
            {
                storage.DeleteOldestCommitted();
                retained--;
            }
            return ServiceCycleReplayExportWriteResult.Succeeded(encoded, retained);
        }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception))
        {
            if (segment is not null) TryDiscard(storage, segment);
            return committed
                ? ServiceCycleReplayExportWriteResult.CommittedThenFaulted(encoded, retained)
                : ServiceCycleReplayExportWriteResult.Faulted(retained);
        }
    }

    private static void TryDiscard(IRestartAwareTraceSegmentStorage storage, object segment)
    {
        try { storage.DiscardSegment(segment); }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception)) { }
    }
}

internal readonly struct ServiceCycleReplayExportWriteResult
{
    private ServiceCycleReplayExportWriteResult(bool success, bool committed, int bytes, int retained)
    {
        Success = success;
        Committed = committed;
        Bytes = bytes;
        Retained = retained;
    }

    internal bool Success { get; }
    internal bool Committed { get; }
    internal int Bytes { get; }
    internal int Retained { get; }
    internal static ServiceCycleReplayExportWriteResult Succeeded(int bytes, int retained) =>
        new(true, true, bytes, retained);
    internal static ServiceCycleReplayExportWriteResult CommittedThenFaulted(int bytes, int retained) =>
        new(false, true, bytes, retained);
    internal static ServiceCycleReplayExportWriteResult Faulted(int retained) =>
        new(false, false, 0, retained);
}
