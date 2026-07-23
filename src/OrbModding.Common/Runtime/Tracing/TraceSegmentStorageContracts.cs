using System;
using System.IO;

namespace OrbModding.Common.Runtime.Tracing;

/// <summary>
/// Storage seam for trace writers. Segments are written under temporary names and atomically
/// committed so readers cannot observe partial artifacts. Calls occur only on background workers.
/// </summary>
public interface ITraceSegmentStorage
{
    object BeginSegment(int ordinal);
    void Append(object segment, ReadOnlySpan<byte> record);
    void CommitSegment(object segment);
    void DiscardSegment(object segment);
    void DeleteOldestCommitted();
}

/// <summary>Persistent-storage extension required by restartable snapshot exporters.</summary>
public interface IRestartAwareTraceSegmentStorage : ITraceSegmentStorage
{
    TraceSegmentStorageRecovery Reconcile(int maximumCommittedSegments);
}

/// <summary>Exact background-worker result of persistent trace-storage reconciliation.</summary>
public readonly struct TraceSegmentStorageRecovery
{
    public TraceSegmentStorageRecovery(
        int nextOrdinal,
        int retainedSegments,
        int startupPrunedSegments,
        int staleTemporaryFilesRemoved)
    {
        if (nextOrdinal < 0 || retainedSegments < 0 || startupPrunedSegments < 0 ||
            staleTemporaryFilesRemoved < 0)
            throw new ArgumentOutOfRangeException(nameof(nextOrdinal));
        NextOrdinal = nextOrdinal;
        RetainedSegments = retainedSegments;
        StartupPrunedSegments = startupPrunedSegments;
        StaleTemporaryFilesRemoved = staleTemporaryFilesRemoved;
    }

    public int NextOrdinal { get; }
    public int RetainedSegments { get; }
    public int StartupPrunedSegments { get; }
    public int StaleTemporaryFilesRemoved { get; }
}

internal sealed class TraceSegmentOrdinalExhaustedException : IOException
{
    internal TraceSegmentOrdinalExhaustedException()
        : base("Trace segment ordinal space is exhausted.") { }
}
