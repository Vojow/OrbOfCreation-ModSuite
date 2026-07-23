using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

public enum DecisionJournalStatusState
{
    Unavailable = 0,
    Initializing = 1,
    Arming = 2,
    Recording = 3,
    Stopping = 4,
    Stopped = 5,
    Faulted = 6,
}

public enum DecisionJournalStatusResult
{
    None = 0,
    BufferExhausted = 1,
    SequenceExhausted = 2,
    InitializationFailed = 3,
    WriteFailed = 4,
    CompletionFailed = 5,
    ProducerFailed = 6,
    RetentionFailed = 7,
    OrdinalExhausted = 8,
}

public readonly struct DecisionJournalStatus : IEquatable<DecisionJournalStatus>
{
    public DecisionJournalStatus(
        DecisionJournalStatusState state,
        long acceptedRecords,
        long writtenRecords,
        long discardedRecords,
        long bytesWritten,
        long writtenSegments,
        int retainedSegments,
        long evictedSegments,
        int startupPrunedSegments,
        int staleTemporaryFilesRemoved,
        int pendingBlocks,
        int peakPendingBlocks,
        long firstIncompleteSequence,
        DecisionJournalStatusResult result,
        string artifactName)
    {
        if (state < DecisionJournalStatusState.Unavailable || state > DecisionJournalStatusState.Faulted)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (result < DecisionJournalStatusResult.None || result > DecisionJournalStatusResult.OrdinalExhausted)
            throw new ArgumentOutOfRangeException(nameof(result));
        if (acceptedRecords < 0 || writtenRecords < 0 || discardedRecords < 0 ||
            writtenRecords > acceptedRecords || discardedRecords > acceptedRecords - writtenRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedRecords));
        }
        if (bytesWritten < 0 || writtenSegments < 0 || writtenSegments > writtenRecords ||
            retainedSegments < 0 || evictedSegments < 0 ||
            startupPrunedSegments < 0 || staleTemporaryFilesRemoved < 0 || pendingBlocks < 0 ||
            peakPendingBlocks < pendingBlocks || firstIncompleteSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesWritten));
        }

        artifactName ??= string.Empty;
        if (artifactName.Length != 0 && !IsSafeArtifactName(artifactName))
            throw new ArgumentException("The journal artifact must be a bounded machine-neutral basename.", nameof(artifactName));
        var unavailable = state == DecisionJournalStatusState.Unavailable;
        var healthy = state is DecisionJournalStatusState.Initializing or DecisionJournalStatusState.Arming or
            DecisionJournalStatusState.Recording or DecisionJournalStatusState.Stopped;
        var terminal = state is DecisionJournalStatusState.Stopped or DecisionJournalStatusState.Faulted;
        if (unavailable != (artifactName.Length == 0) ||
            (result == DecisionJournalStatusResult.None) != (firstIncompleteSequence == 0) ||
            unavailable && !HasZeroMetrics(
                acceptedRecords,
                writtenRecords,
                discardedRecords,
                bytesWritten,
                writtenSegments,
                retainedSegments,
                evictedSegments,
                startupPrunedSegments,
                staleTemporaryFilesRemoved,
                pendingBlocks,
                peakPendingBlocks,
                firstIncompleteSequence) ||
            healthy && result != DecisionJournalStatusResult.None ||
            state == DecisionJournalStatusState.Faulted && result == DecisionJournalStatusResult.None ||
            terminal && (pendingBlocks != 0 || acceptedRecords != writtenRecords + discardedRecords) ||
            state == DecisionJournalStatusState.Stopped && discardedRecords != 0)
        {
            throw new ArgumentException("The decision-journal status fields are inconsistent.", nameof(state));
        }

        State = state;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        DiscardedRecords = discardedRecords;
        BytesWritten = bytesWritten;
        WrittenSegments = writtenSegments;
        RetainedSegments = retainedSegments;
        EvictedSegments = evictedSegments;
        StartupPrunedSegments = startupPrunedSegments;
        StaleTemporaryFilesRemoved = staleTemporaryFilesRemoved;
        PendingBlocks = pendingBlocks;
        PeakPendingBlocks = peakPendingBlocks;
        FirstIncompleteSequence = firstIncompleteSequence;
        Result = result;
        ArtifactName = artifactName;
    }

    public static DecisionJournalStatus Unavailable => new(
        DecisionJournalStatusState.Unavailable,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        DecisionJournalStatusResult.None,
        string.Empty);

    public DecisionJournalStatusState State { get; }
    public long AcceptedRecords { get; }
    public long WrittenRecords { get; }
    public long DiscardedRecords { get; }
    public long BytesWritten { get; }
    public long WrittenSegments { get; }
    public int RetainedSegments { get; }
    public long EvictedSegments { get; }
    public int StartupPrunedSegments { get; }
    public int StaleTemporaryFilesRemoved { get; }
    public int PendingBlocks { get; }
    public int PeakPendingBlocks { get; }
    public long FirstIncompleteSequence { get; }
    public DecisionJournalStatusResult Result { get; }
    public string ArtifactName { get; }

    public bool Equals(DecisionJournalStatus other) =>
        State == other.State && AcceptedRecords == other.AcceptedRecords &&
        WrittenRecords == other.WrittenRecords && DiscardedRecords == other.DiscardedRecords &&
        BytesWritten == other.BytesWritten && WrittenSegments == other.WrittenSegments &&
        RetainedSegments == other.RetainedSegments && EvictedSegments == other.EvictedSegments &&
        StartupPrunedSegments == other.StartupPrunedSegments &&
        StaleTemporaryFilesRemoved == other.StaleTemporaryFilesRemoved &&
        PendingBlocks == other.PendingBlocks && PeakPendingBlocks == other.PeakPendingBlocks &&
        FirstIncompleteSequence == other.FirstIncompleteSequence && Result == other.Result &&
        string.Equals(ArtifactName, other.ArtifactName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DecisionJournalStatus other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(State, AcceptedRecords, WrittenRecords, DiscardedRecords, BytesWritten),
        HashCode.Combine(WrittenSegments, RetainedSegments, EvictedSegments, StartupPrunedSegments),
        HashCode.Combine(StaleTemporaryFilesRemoved, PendingBlocks, PeakPendingBlocks, FirstIncompleteSequence),
        Result,
        ArtifactName);

    public static bool operator ==(DecisionJournalStatus left, DecisionJournalStatus right) => left.Equals(right);
    public static bool operator !=(DecisionJournalStatus left, DecisionJournalStatus right) => !left.Equals(right);

    internal static bool IsSafeArtifactName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value is "." or "..") return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }

    private static bool HasZeroMetrics(
        long acceptedRecords,
        long writtenRecords,
        long discardedRecords,
        long bytesWritten,
        long writtenSegments,
        int retainedSegments,
        long evictedSegments,
        int startupPrunedSegments,
        int staleTemporaryFilesRemoved,
        int pendingBlocks,
        int peakPendingBlocks,
        long firstIncompleteSequence) =>
        acceptedRecords == 0 && writtenRecords == 0 && discardedRecords == 0 && bytesWritten == 0 &&
        writtenSegments == 0 && retainedSegments == 0 && evictedSegments == 0 &&
        startupPrunedSegments == 0 && staleTemporaryFilesRemoved == 0 && pendingBlocks == 0 &&
        peakPendingBlocks == 0 && firstIncompleteSequence == 0;
}

public interface IDecisionJournalStatusSource
{
    DecisionJournalStatus Status { get; }
    long Revision { get; }
}
