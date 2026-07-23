using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal sealed class ServiceCycleReplayFrozenSnapshotStager
{
    private readonly ServiceCycleSemanticTraceSource _source;
    private ServiceCycleReplayExportSlot? _slot;
    private ServiceCycleTraceCursor _cursor;
    private ServiceCycleTraceCursor _terminal;

    internal ServiceCycleReplayFrozenSnapshotStager(ServiceCycleSemanticTraceSource source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    internal bool TryBegin(
        ServiceCycleReplayExportSlot first,
        ServiceCycleReplayExportSlot second)
    {
        if (_slot is not null) return true;
        var slot = ServiceCycleReplayExportSlotPool.TryClaim(first, second);
        if (slot is null) return false;
        _slot = slot;
        _cursor = default;
        _terminal = _source.Cursor;
        return true;
    }

    internal bool SourceIsFrozen => _source.Cursor == _terminal;
    internal ServiceCycleTraceSessionId Session => _terminal.Session;

    internal bool CopyNext(int maximumEvents, out int copied)
    {
        var slot = _slot ?? throw new InvalidOperationException("No replay snapshot is being staged.");
        if (_cursor == _terminal)
        {
            copied = 0;
            return true;
        }
        var copyCount = Math.Min(maximumEvents, slot.Events.Length - slot.EventCount);
        if (copyCount == 0)
            throw new InvalidOperationException("The semantic snapshot exceeded its export slot.");
        var drain = _source.DrainSince(
            _cursor,
            slot.Events.AsSpan(slot.EventCount, copyCount));
        if (drain.Dropped.IsPresent)
        {
            if (slot.EventCount != 0 || slot.Dropped.IsPresent)
                throw new InvalidOperationException(
                    "A frozen semantic source changed while its snapshot was being copied.");
            slot.Dropped = drain.Dropped;
        }
        slot.SemanticSession = drain.Session;
        slot.EventCount = checked(slot.EventCount + drain.Copied);
        _cursor = drain.Cursor;
        copied = drain.Copied;
        return _cursor == _terminal;
    }

    internal ServiceCycleReplayExportSlot Complete(
        in ServiceCycleReplayRecordingSnapshot recording,
        int ordinal)
    {
        var slot = _slot ?? throw new InvalidOperationException("No replay snapshot is being staged.");
        slot.SemanticSession = _terminal.Session;
        slot.Recording = recording;
        slot.Ordinal = ordinal;
        ClearOwnership();
        return slot;
    }

    internal void Release()
    {
        if (_slot is not { } slot) return;
        ClearOwnership();
        ServiceCycleReplayExportSlotPool.Release(slot);
    }

    private void ClearOwnership()
    {
        _slot = null;
        _cursor = default;
        _terminal = default;
    }
}
