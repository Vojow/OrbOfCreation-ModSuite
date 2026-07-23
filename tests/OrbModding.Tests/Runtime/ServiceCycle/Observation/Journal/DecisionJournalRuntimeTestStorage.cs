using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

internal sealed class DecisionJournalRuntimeTestStorage : IRestartAwareTraceSegmentStorage, IDisposable
{
    private readonly object _gate = new();
    private readonly bool _blockReconcile;
    private readonly bool _blockCommit;
    private readonly bool _failCommit;
    private readonly List<byte[]> _segments = new();

    internal DecisionJournalRuntimeTestStorage(
        bool blockReconcile = false,
        bool blockCommit = false,
        bool failCommit = false)
    {
        _blockReconcile = blockReconcile;
        _blockCommit = blockCommit;
        _failCommit = failCommit;
    }

    internal ManualResetEventSlim ReconcileEntered { get; } = new();
    internal ManualResetEventSlim ReconcileRelease { get; } = new();
    internal ManualResetEventSlim ReconcileCompleted { get; } = new();
    internal ManualResetEventSlim CommitEntered { get; } = new();
    internal ManualResetEventSlim CommitRelease { get; } = new();
    internal int ReconcileThreadId { get; private set; }

    internal byte[][] Segments
    {
        get
        {
            lock (_gate) return _segments.ToArray();
        }
    }

    internal DecisionJournalRecord[] ReadRecords()
    {
        var records = new List<DecisionJournalRecord>();
        foreach (var segment in Segments)
            records.AddRange(DecisionJournalSegmentCodec.Decode(segment).Records);
        return records.ToArray();
    }

    public TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments)
    {
        ReconcileThreadId = Environment.CurrentManagedThreadId;
        ReconcileEntered.Set();
        if (_blockReconcile) ReconcileRelease.Wait();
        ReconcileCompleted.Set();
        return default;
    }

    public object BeginSegment(int ordinal) => new MemorySegment();

    public void Append(object segment, ReadOnlySpan<byte> record) =>
        ((MemorySegment)segment).Bytes.AddRange(record.ToArray());

    public void CommitSegment(object segment)
    {
        CommitEntered.Set();
        if (_blockCommit) CommitRelease.Wait();
        if (_failCommit) throw new InvalidOperationException("Injected journal commit failure.");
        lock (_gate) _segments.Add(((MemorySegment)segment).Bytes.ToArray());
    }

    public void DiscardSegment(object segment) { }

    public void DeleteOldestCommitted()
    {
        lock (_gate)
        {
            if (_segments.Count == 0)
                throw new InvalidOperationException("No committed journal segment can be deleted.");
            _segments.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        ReconcileRelease.Set();
        CommitRelease.Set();
    }

    private sealed class MemorySegment
    {
        internal List<byte> Bytes { get; } = new();
    }
}
