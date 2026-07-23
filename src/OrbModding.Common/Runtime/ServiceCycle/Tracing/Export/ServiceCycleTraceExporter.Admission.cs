using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

public sealed partial class ServiceCycleTraceExporter
{
    /// <summary>
    /// Requests one coherent snapshot without waiting. Backpressure is decided before the recorder is
    /// touched, and an accepted request performs no encoding or storage work on the owner thread.
    /// </summary>
    public ServiceCycleTraceExportRequestResult RequestSnapshot()
    {
        EnsureOwner();
        if (!TryBeginOwnerOperation()) return RejectUnavailable(ReadStatus());
        var status = ReadStatus();
        if (status != ServiceCycleTraceExportStatus.Running)
        {
            EndOwnerOperation();
            return RejectUnavailable(status);
        }
        if (_source.EmissionFaulted)
        {
            LatchSourceFault();
            try { _wake!.Set(); }
            finally { EndOwnerOperation(); }
            return RejectUnavailable(ServiceCycleTraceExportStatus.Faulted);
        }

        var slot = TryClaimSlot();
        if (slot is null)
        {
            Interlocked.Increment(ref _backpressureRejections);
            EndOwnerOperation();
            return ServiceCycleTraceExportRequestResult.Backpressured;
        }

        try
        {
            if (_nextOrdinal == int.MaxValue)
            {
                Volatile.Write(ref slot.State, ServiceCycleTraceExportSlot.Free);
                LatchSourceFault();
                _wake!.Set();
                EndOwnerOperation();
                return RejectUnavailable(ServiceCycleTraceExportStatus.Faulted);
            }
            var drain = _source.DrainSince(default, slot.Events);
            if (drain.HasMore)
                throw new InvalidOperationException("A full-capacity semantic snapshot drain must be complete.");

            slot.Session = drain.Session;
            slot.Dropped = drain.Dropped;
            slot.EventCount = drain.Copied;
            slot.ServiceCapacity = _source.ServiceCapacity;
            slot.Ordinal = _nextOrdinal;
            _nextOrdinal = checked(_nextOrdinal + 1);
            Interlocked.Increment(ref _pendingSnapshots);
            Interlocked.Increment(ref _acceptedSnapshots);
            Volatile.Write(ref slot.State, ServiceCycleTraceExportSlot.Ready);

            // Signal before ending the admission handshake. Worker cleanup closes admission and waits
            // for this bounded owner operation before reclaiming the wake handle.
            _wake!.Set();
            EndOwnerOperation();
            return ServiceCycleTraceExportRequestResult.Accepted;
        }
        catch
        {
            Volatile.Write(ref slot.State, ServiceCycleTraceExportSlot.Free);
            EndOwnerOperation();
            throw;
        }
    }

    private ServiceCycleTraceExportSlot? TryClaimSlot()
    {
        if (Interlocked.CompareExchange(
                ref _first!.State,
                ServiceCycleTraceExportSlot.OwnerClaimed,
                ServiceCycleTraceExportSlot.Free) == ServiceCycleTraceExportSlot.Free)
        {
            return _first;
        }

        return Interlocked.CompareExchange(
                ref _second!.State,
                ServiceCycleTraceExportSlot.OwnerClaimed,
                ServiceCycleTraceExportSlot.Free) == ServiceCycleTraceExportSlot.Free
            ? _second
            : null;
    }

    private ServiceCycleTraceExportRequestResult RejectUnavailable(ServiceCycleTraceExportStatus status)
    {
        Interlocked.Increment(ref _unavailableRejections);
        return status switch
        {
            ServiceCycleTraceExportStatus.Disabled => ServiceCycleTraceExportRequestResult.Disabled,
            ServiceCycleTraceExportStatus.Initializing => ServiceCycleTraceExportRequestResult.Initializing,
            ServiceCycleTraceExportStatus.Stopping => ServiceCycleTraceExportRequestResult.Stopping,
            ServiceCycleTraceExportStatus.Stopped => ServiceCycleTraceExportRequestResult.Stopped,
            ServiceCycleTraceExportStatus.Faulted => ServiceCycleTraceExportRequestResult.Faulted,
            _ => throw new InvalidOperationException("The exporter has an unknown lifecycle state."),
        };
    }
}
