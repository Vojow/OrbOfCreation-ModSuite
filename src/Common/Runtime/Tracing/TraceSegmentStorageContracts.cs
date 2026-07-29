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

/// <summary>
/// Judges whether a store's newest committed segment is one this writer can continue from.
/// </summary>
/// <remarks>
/// Storage owns file names and ordinals, never payload formats, so the writer supplies the judgment
/// and storage only acts on it. Only the newest segment is offered: it is the one the next segment
/// has to agree with, and reading a full store on the startup path is not free.
/// </remarks>
public interface ITraceSegmentHeaderProbe
{
    int HeaderBytes { get; }

    bool IsCompatible(ReadOnlySpan<byte> header);
}

/// <summary>Persistent-storage extension required by restartable snapshot exporters.</summary>
public interface IRestartAwareTraceSegmentStorage : ITraceSegmentStorage
{
    TraceSegmentStorageRecovery Reconcile(
        int maximumCommittedSegments,
        ITraceSegmentHeaderProbe? probe = null);
}

/// <summary>Exact background-worker result of persistent trace-storage reconciliation.</summary>
public readonly struct TraceSegmentStorageRecovery
{
    public TraceSegmentStorageRecovery(
        int nextOrdinal,
        int retainedSegments,
        int startupPrunedSegments,
        int staleTemporaryFilesRemoved,
        int incompatibleSegmentsPruned = 0)
    {
        if (nextOrdinal < 0 || retainedSegments < 0 || startupPrunedSegments < 0 ||
            staleTemporaryFilesRemoved < 0 || incompatibleSegmentsPruned < 0)
            throw new ArgumentOutOfRangeException(nameof(nextOrdinal));
        NextOrdinal = nextOrdinal;
        RetainedSegments = retainedSegments;
        StartupPrunedSegments = startupPrunedSegments;
        StaleTemporaryFilesRemoved = staleTemporaryFilesRemoved;
        IncompatibleSegmentsPruned = incompatibleSegmentsPruned;
    }

    public int NextOrdinal { get; }
    public int RetainedSegments { get; }
    public int StartupPrunedSegments { get; }
    public int StaleTemporaryFilesRemoved { get; }

    /// <summary>Committed segments discarded because this writer could not continue from them.</summary>
    public int IncompatibleSegmentsPruned { get; }
}

internal sealed class TraceSegmentOrdinalExhaustedException : IOException
{
    internal TraceSegmentOrdinalExhaustedException()
        : base("Trace segment ordinal space is exhausted.") { }
}
