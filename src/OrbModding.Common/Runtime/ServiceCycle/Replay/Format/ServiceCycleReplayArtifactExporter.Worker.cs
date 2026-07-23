using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public sealed partial class ServiceCycleReplayArtifactExporter
{
    private void WorkerLoop()
    {
        var startupCompleted = false;
        try
        {
            var recovery = _storage.Reconcile(_maximumCommitted);
            _nextOrdinal = recovery.NextOrdinal;
            Volatile.Write(ref _retained, recovery.RetainedSegments);
            Volatile.Write(ref _startupPruned, recovery.StartupPrunedSegments);
            Volatile.Write(ref _staleTemporaryRemoved, recovery.StaleTemporaryFilesRemoved);
            if (ReadStatus() != ServiceCycleReplayExportStatus.Initializing)
            {
                Interlocked.CompareExchange(
                    ref _status,
                    (int)ServiceCycleReplayExportStatus.Stopped,
                    (int)ServiceCycleReplayExportStatus.Stopping);
                return;
            }
            _first = new ServiceCycleReplayExportSlot(_semanticCapacity);
            _second = new ServiceCycleReplayExportSlot(_semanticCapacity);
            startupCompleted = true;
            Interlocked.CompareExchange(
                ref _status,
                (int)ServiceCycleReplayExportStatus.Running,
                (int)ServiceCycleReplayExportStatus.Initializing);
            while (true)
            {
                var slot = ServiceCycleReplayExportSlotPool.TryTakeNextReady(_first!, _second!);
                if (slot is not null)
                {
                    if (!Export(slot)) return;
                    continue;
                }
                var status = ReadStatus();
                if (status == ServiceCycleReplayExportStatus.Running)
                {
                    _wake!.WaitOne();
                    continue;
                }
                if (status == ServiceCycleReplayExportStatus.Stopping)
                {
                    CloseAdmissionAndWait();
                    if (ServiceCycleReplayExportSlotPool.TryTakeNextReady(_first!, _second!) is { } final)
                    {
                        if (!Export(final)) return;
                        continue;
                    }
                    Interlocked.CompareExchange(
                        ref _status,
                        (int)ServiceCycleReplayExportStatus.Stopped,
                        (int)ServiceCycleReplayExportStatus.Stopping);
                }
                return;
            }
        }
        catch (TraceSegmentOrdinalExhaustedException)
        {
            LatchFault(null, ServiceCycleReplayExporterFaultReason.OrdinalExhausted);
        }
        catch (Exception exception) when (!ServiceCycleReplayExportFailurePolicy.IsProcessFatal(exception))
        {
            var reason = startupCompleted
                ? ServiceCycleReplayExporterFaultReason.WorkerFailure
                : ServiceCycleReplayExporterFaultReason.StartupFailure;
            LatchFault(null, reason);
        }
        finally
        {
            CloseAdmissionAndWait();
            _wake?.Dispose();
        }
    }

    private bool Export(ServiceCycleReplayExportSlot slot)
    {
        var ordinal = slot.Ordinal;
        var result = ServiceCycleReplayExportWriter.Write(
            _recording,
            _storage,
            slot,
            _retained,
            _maximumCommitted);
        Volatile.Write(ref _retained, result.Retained);
        if (result.Committed)
        {
            Interlocked.Increment(ref _exported);
            Interlocked.Add(ref _bytesWritten, result.Bytes);
            Interlocked.Decrement(ref _pending);
            ServiceCycleReplayExportSlotPool.Release(slot);
            NotifyCommitted(ordinal, result.Bytes);
        }
        if (result.Success) return true;
        LatchFault(
            result.Committed ? null : slot,
            result.Committed
                ? ServiceCycleReplayExporterFaultReason.RetentionFailure
                : ServiceCycleReplayExporterFaultReason.EncodingOrStorageFailure);
        return false;
    }
}
