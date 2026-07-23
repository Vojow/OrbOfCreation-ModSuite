using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

public sealed partial class ServiceCycleTraceExporter
{
    private bool Export(ServiceCycleTraceExportSlot slot)
    {
        object? segment = null;
        var committed = false;
        var encoded = 0;
        try
        {
            encoded = ServiceCycleTraceCodec.Encode(
                slot.Session,
                slot.Dropped,
                slot.ServiceCapacity,
                slot.Events.AsSpan(0, slot.EventCount),
                _encodingBuffer!);
            segment = _storage.BeginSegment(slot.Ordinal);
            _storage.Append(segment, _encodingBuffer!.AsSpan(0, encoded));
            _storage.CommitSegment(segment);
            segment = null;
            committed = true;
            _retainedSnapshots++;
            if (_retainedSnapshots > _maximumCommittedSnapshots)
            {
                _storage.DeleteOldestCommitted();
                _retainedSnapshots--;
            }

            Interlocked.Increment(ref _exportedSnapshots);
            Interlocked.Add(ref _bytesWritten, encoded);
            Interlocked.Decrement(ref _pendingSnapshots);
            Release(slot);
            return true;
        }
        catch
        {
            if (committed)
            {
                Interlocked.Increment(ref _exportedSnapshots);
                Interlocked.Add(ref _bytesWritten, encoded);
                Interlocked.Decrement(ref _pendingSnapshots);
                Release(slot);
                LatchFault(null);
                return false;
            }
            LatchFault(slot);
            if (segment is not null) TryDiscard(segment);
            return false;
        }
    }

    private void LatchFault(ServiceCycleTraceExportSlot? failedSlot)
    {
        Interlocked.Exchange(ref _status, (int)ServiceCycleTraceExportStatus.Faulted);
        Interlocked.CompareExchange(ref _faultCount, 1, 0);
        CloseAdmissionAndWait();

        if (failedSlot is not null) DiscardAccepted(failedSlot);
        DiscardIfReady(_first!);
        DiscardIfReady(_second!);
    }

    private void DiscardIfReady(ServiceCycleTraceExportSlot slot)
    {
        if (Interlocked.CompareExchange(
                ref slot.State,
                ServiceCycleTraceExportSlot.Free,
                ServiceCycleTraceExportSlot.Ready) == ServiceCycleTraceExportSlot.Ready)
        {
            Interlocked.Increment(ref _discardedSnapshots);
            Interlocked.Decrement(ref _pendingSnapshots);
            slot.EventCount = 0;
            slot.ServiceCapacity = 0;
            slot.Dropped = default;
            slot.Session = default;
        }
    }

    private void DiscardAccepted(ServiceCycleTraceExportSlot slot)
    {
        Interlocked.Increment(ref _discardedSnapshots);
        Interlocked.Decrement(ref _pendingSnapshots);
        Release(slot);
    }

    private static void Release(ServiceCycleTraceExportSlot slot)
    {
        slot.EventCount = 0;
        slot.ServiceCapacity = 0;
        slot.Dropped = default;
        slot.Session = default;
        Volatile.Write(ref slot.State, ServiceCycleTraceExportSlot.Free);
    }

    private void TryDiscard(object segment)
    {
        try { _storage.DiscardSegment(segment); }
        catch { }
    }
}
