using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public sealed partial class ServiceCycleReplayArtifactExporter
{
    public ServiceCycleReplayExportRequestResult RequestSnapshot() =>
        ContinueSnapshot(_semantic.Capacity, retainOnSnapshotContention: false, out _);

    internal ServiceCycleReplayExportRequestResult ContinueFrozenSnapshot(
        int maximumSemanticEvents,
        out int copiedEvents) =>
        ContinueSnapshot(maximumSemanticEvents, retainOnSnapshotContention: true, out copiedEvents);

    private ServiceCycleReplayExportRequestResult ContinueSnapshot(
        int maximumSemanticEvents,
        bool retainOnSnapshotContention,
        out int copiedEvents)
    {
        copiedEvents = 0;
        EnsureOwner();
        if (maximumSemanticEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSemanticEvents));
        if (!TryBeginOwnerOperation()) return RejectUnavailable(ReadStatus());
        try
        {
            var status = ReadStatus();
            if (status != ServiceCycleReplayExportStatus.Running)
                return RejectUnavailable(status);
            if (_semantic.EmissionFaulted)
            {
                ReleaseStagedSnapshot();
                LatchOwnerFault(ServiceCycleReplayExporterFaultReason.SourceFault);
                _wake!.Set();
                return RejectUnavailable(ServiceCycleReplayExportStatus.Faulted);
            }
            if (!_stager.TryBegin(_first!, _second!))
            {
                Interlocked.Increment(ref _backpressured);
                return ServiceCycleReplayExportRequestResult.Backpressured;
            }
            if (!_stager.SourceIsFrozen)
            {
                ReleaseStagedSnapshot();
                LatchOwnerFault(ServiceCycleReplayExporterFaultReason.SourceFault);
                _wake!.Set();
                return RejectUnavailable(ServiceCycleReplayExportStatus.Faulted);
            }
            if (_nextOrdinal == int.MaxValue)
            {
                ReleaseStagedSnapshot();
                LatchOwnerFault(ServiceCycleReplayExporterFaultReason.OrdinalExhausted);
                _wake!.Set();
                return RejectUnavailable(ServiceCycleReplayExportStatus.Faulted);
            }
            if (!_stager.CopyNext(maximumSemanticEvents, out var copiedThisRequest))
            {
                copiedEvents = copiedThisRequest;
                Interlocked.Add(ref _semanticEventsCopied, copiedThisRequest);
                UpdatePeakCopied(copiedThisRequest);
                return ServiceCycleReplayExportRequestResult.Copying;
            }
            copiedEvents = copiedThisRequest;
            Interlocked.Add(ref _semanticEventsCopied, copiedThisRequest);
            UpdatePeakCopied(copiedThisRequest);
            if (!_recording.TryReadSnapshot(out var recordingSnapshot))
            {
                if (!retainOnSnapshotContention) ReleaseStagedSnapshot();
                Interlocked.Increment(ref _snapshotContended);
                return ServiceCycleReplayExportRequestResult.SnapshotContended;
            }
            if (recordingSnapshot.TraceSession != _stager.Session)
                throw new InvalidOperationException("Semantic and replay snapshot sessions diverged.");
            var slot = _stager.Complete(in recordingSnapshot, _nextOrdinal);
            _nextOrdinal = checked(_nextOrdinal + 1);
            Interlocked.Increment(ref _pending);
            Interlocked.Increment(ref _accepted);
            Volatile.Write(ref slot.State, ServiceCycleReplayExportSlot.Ready);
            _wake!.Set();
            return ServiceCycleReplayExportRequestResult.Accepted;
        }
        catch
        {
            ReleaseStagedSnapshot();
            throw;
        }
        finally
        {
            EndOwnerOperation();
        }
    }

    private void UpdatePeakCopied(int copied)
    {
        var current = Volatile.Read(ref _peakSemanticEventsCopiedPerRequest);
        while (copied > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _peakSemanticEventsCopiedPerRequest,
                copied,
                current);
            if (observed == current) return;
            current = observed;
        }
    }

    private ServiceCycleReplayExportRequestResult RejectUnavailable(ServiceCycleReplayExportStatus status)
    {
        Interlocked.Increment(ref _unavailable);
        return status switch
        {
            ServiceCycleReplayExportStatus.Disabled => ServiceCycleReplayExportRequestResult.Disabled,
            ServiceCycleReplayExportStatus.Initializing => ServiceCycleReplayExportRequestResult.Initializing,
            ServiceCycleReplayExportStatus.Stopping => ServiceCycleReplayExportRequestResult.Stopping,
            ServiceCycleReplayExportStatus.Stopped => ServiceCycleReplayExportRequestResult.Stopped,
            ServiceCycleReplayExportStatus.Faulted => ServiceCycleReplayExportRequestResult.Faulted,
            _ => throw new InvalidOperationException("Unknown replay exporter state."),
        };
    }

    private ServiceCycleReplayExportStatus ReadStatus() =>
        (ServiceCycleReplayExportStatus)Volatile.Read(ref _status);

    private bool TryBeginOwnerOperation()
    {
        if (Volatile.Read(ref _admissionClosed) != 0) return false;
        if (Interlocked.CompareExchange(ref _ownerOperationActive, 1, 0) != 0) return false;
        if (Volatile.Read(ref _admissionClosed) == 0) return true;
        Volatile.Write(ref _ownerOperationActive, 0);
        return false;
    }

    private void EndOwnerOperation() => Volatile.Write(ref _ownerOperationActive, 0);
}
