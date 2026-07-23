using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

/// <summary>
/// One main-to-worker detached-record handoff paired with one physical ordinary runner. Ordinary runner
/// ownership supplies the happens-before edge; this bridge adds only mismatch evidence and never locks.
/// </summary>
internal sealed class ServiceCycleReplayInputBridge<TCycleInputRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleReplaySession _session;
    private readonly ulong _lifecycle;
    private TCycleInputRecord _record;
    private ServiceCycleReplayCycleKey _cycle;
    private int _traceServiceKey;
    private bool _hasRecord;
    private bool _recordMissing;
    private int _released;
    private int _frameReady;

    internal ServiceCycleReplayInputBridge(
        ServiceCycleReplaySession session,
        ulong lifecycle)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (lifecycle == 0) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        _lifecycle = lifecycle;
    }

    internal ulong Lifecycle => _lifecycle;
    internal bool IsReleased => Volatile.Read(ref _released) != 0;
    internal bool IsFrameReady => Volatile.Read(ref _frameReady) != 0;
    internal int TraceServiceKey => Volatile.Read(ref _traceServiceKey);

    internal void BindTraceServiceKey(int traceServiceKey)
    {
        if (traceServiceKey <= 0) throw new ArgumentOutOfRangeException(nameof(traceServiceKey));
        var previous = Interlocked.CompareExchange(ref _traceServiceKey, traceServiceKey, 0);
        if (previous != 0 && previous != traceServiceKey)
            throw new InvalidOperationException("A physical replay bridge cannot change trace service identity.");
    }

    internal void MarkFrameReady() => Volatile.Write(ref _frameReady, 1);

    internal void Publish(in ServiceCycleReplayCycleKey cycle, in TCycleInputRecord record)
    {
        if (IsReleased) return;
        MarkOverwrittenInputIncomplete();
        _record = record;
        _cycle = cycle;
        _recordMissing = false;
        _hasRecord = true;
    }

    internal void PublishMissing(in ServiceCycleReplayCycleKey cycle)
    {
        if (IsReleased) return;
        MarkOverwrittenInputIncomplete();
        _record = default;
        _cycle = cycle;
        _recordMissing = true;
        _hasRecord = false;
        _session.MarkRequiredRecordMissing(
            in cycle,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
    }

    internal bool TryTake(
        in ServiceCycleContext context,
        out ServiceCycleReplayCycleKey cycle,
        out TCycleInputRecord record)
    {
        var traceServiceKey = TraceServiceKey;
        if (traceServiceKey <= 0)
            throw new InvalidOperationException("Replay trace identity was not bound before evaluation.");
        var identity = context.Identity;
        var expected = new ServiceCycleReplayCycleKey(traceServiceKey, in identity);
        cycle = expected;
        if (_cycle != expected || _recordMissing || !_hasRecord)
        {
            _session.MarkRequiredRecordMissing(
                in expected,
                new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
            Clear();
            record = default;
            return false;
        }

        record = _record;
        Clear();
        return true;
    }

    internal void MarkReleased()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        Clear();
    }

    private void MarkOverwrittenInputIncomplete()
    {
        if (!_cycle.IsValid || _recordMissing) return;
        _session.MarkRequiredRecordMissing(
            in _cycle,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0));
    }

    private void Clear()
    {
        _record = default;
        _cycle = default;
        _hasRecord = false;
        _recordMissing = false;
    }
}
