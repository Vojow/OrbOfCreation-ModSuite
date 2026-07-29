using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;

public enum ManualFullTraceState
{
    Unavailable = 0,
    Idle = 1,
    Arming = 2,
    Recording = 3,
    Stopping = 4,
    Complete = 5,
    Incomplete = 6,
}

public enum ManualFullTraceResult
{
    None = 0,
    UserStopped = 1,
    RuntimeShutdown = 2,
    BufferExhausted = 3,
    SequenceExhausted = 4,
    InitializationFailed = 5,
    WriteFailed = 6,
    CompletionFailed = 7,
    SemanticFault = 8,
}

public enum ManualFullTraceCommand
{
    None = 0,
    Start = 1,
    Stop = 2,
}

public enum ManualFullTraceCommandResult
{
    Accepted = 0,
    Unavailable = 1,
    CommandPending = 2,
    InvalidState = 3,
}

public readonly struct ManualFullTraceStatus : IEquatable<ManualFullTraceStatus>
{
    public ManualFullTraceStatus(
        ManualFullTraceState state,
        TimeSpan duration,
        long acceptedRecords,
        long writtenRecords,
        long bytesWritten,
        long segmentCount,
        long firstIncompleteSequence,
        bool manifestCommitted,
        ManualFullTraceResult result,
        string artifactName,
        bool storesLost)
    {
        if (state < ManualFullTraceState.Unavailable || state > ManualFullTraceState.Incomplete)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (result < ManualFullTraceResult.None || result > ManualFullTraceResult.SemanticFault)
            throw new ArgumentOutOfRangeException(nameof(result));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (acceptedRecords < 0 || writtenRecords < 0 || writtenRecords > acceptedRecords)
            throw new ArgumentOutOfRangeException(nameof(acceptedRecords));
        if (bytesWritten < 0 || segmentCount < 0 || segmentCount > writtenRecords)
            throw new ArgumentOutOfRangeException(nameof(bytesWritten));
        if (firstIncompleteSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(firstIncompleteSequence));

        artifactName ??= string.Empty;
        if (artifactName.Length != 0 && !IsSafeArtifactName(artifactName))
            throw new ArgumentException("The artifact name must be a bounded machine-neutral basename.", nameof(artifactName));

        var inactive = state is ManualFullTraceState.Unavailable or ManualFullTraceState.Idle;
        var active = state is ManualFullTraceState.Arming or ManualFullTraceState.Recording or ManualFullTraceState.Stopping;
        var mayHaveNoArtifact = state == ManualFullTraceState.Incomplete &&
            result == ManualFullTraceResult.InitializationFailed;
        if (inactive && (duration != TimeSpan.Zero || acceptedRecords != 0 || writtenRecords != 0 ||
                bytesWritten != 0 || segmentCount != 0 || firstIncompleteSequence != 0 ||
                manifestCommitted || storesLost ||
                result != ManualFullTraceResult.None || artifactName.Length != 0) ||
            !inactive && !mayHaveNoArtifact && string.IsNullOrWhiteSpace(artifactName) ||
            active && (manifestCommitted || result != ManualFullTraceResult.None || firstIncompleteSequence != 0) ||
            state == ManualFullTraceState.Complete &&
                (!manifestCommitted || firstIncompleteSequence != 0 ||
                    result is not (ManualFullTraceResult.UserStopped or ManualFullTraceResult.RuntimeShutdown)) ||
            state == ManualFullTraceState.Incomplete &&
                (firstIncompleteSequence == 0 ||
                    result is ManualFullTraceResult.None or ManualFullTraceResult.UserStopped))
        {
            throw new ArgumentException("The manual full-trace status fields are inconsistent.", nameof(state));
        }

        State = state;
        Duration = duration;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        BytesWritten = bytesWritten;
        SegmentCount = segmentCount;
        FirstIncompleteSequence = firstIncompleteSequence;
        ManifestCommitted = manifestCommitted;
        Result = result;
        ArtifactName = artifactName;
        StoresLost = storesLost;
    }

    public static ManualFullTraceStatus Unavailable => CreateInactive(ManualFullTraceState.Unavailable);
    public static ManualFullTraceStatus Idle => CreateInactive(ManualFullTraceState.Idle);

    public ManualFullTraceState State { get; }
    public TimeSpan Duration { get; }
    public long AcceptedRecords { get; }
    public long WrittenRecords { get; }
    public long BytesWritten { get; }
    public long SegmentCount { get; }
    public long FirstIncompleteSequence { get; }
    public bool ManifestCommitted { get; }
    public ManualFullTraceResult Result { get; }
    public string ArtifactName { get; }

    /// <summary>
    /// True when a publication store could not be written. The recorded events are unaffected, so a
    /// session can complete with this set — but a decision naming a configuration or strategy
    /// generation has nothing beside it saying what that generation held, which is a different
    /// artifact from a complete one and is reported as such.
    /// </summary>
    public bool StoresLost { get; }

    public bool Equals(ManualFullTraceStatus other) =>
        State == other.State && Duration == other.Duration &&
        AcceptedRecords == other.AcceptedRecords && WrittenRecords == other.WrittenRecords &&
        BytesWritten == other.BytesWritten && SegmentCount == other.SegmentCount &&
        FirstIncompleteSequence == other.FirstIncompleteSequence &&
        ManifestCommitted == other.ManifestCommitted && Result == other.Result &&
        StoresLost == other.StoresLost &&
        string.Equals(ArtifactName, other.ArtifactName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ManualFullTraceStatus other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        State,
        Duration,
        AcceptedRecords,
        WrittenRecords,
        BytesWritten,
        SegmentCount,
        HashCode.Combine(FirstIncompleteSequence, ManifestCommitted, Result, ArtifactName, StoresLost));

    public static bool operator ==(ManualFullTraceStatus left, ManualFullTraceStatus right) => left.Equals(right);
    public static bool operator !=(ManualFullTraceStatus left, ManualFullTraceStatus right) => !left.Equals(right);

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

    private static ManualFullTraceStatus CreateInactive(ManualFullTraceState state) =>
        new(state, TimeSpan.Zero, 0, 0, 0, 0, 0, false, ManualFullTraceResult.None, string.Empty, false);
}

public interface IManualFullTraceControl
{
    ManualFullTraceStatus Status { get; }

    ManualFullTraceCommand PendingCommand { get; }

    long Revision { get; }

    ManualFullTraceCommandResult RequestStart();

    ManualFullTraceCommandResult RequestStop();
}
