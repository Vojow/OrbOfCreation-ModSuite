#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;

public enum PerformanceProfileControlState
{
    Unavailable = 0,
    Idle = 1,
    Recording = 2,
    Stopping = 3,
    Complete = 4,
    Faulted = 5,
}

public enum PerformanceProfileResult
{
    None = 0,
    UserStopped = 1,
    RuntimeShutdown = 2,
    BufferExhausted = 3,
    SequenceExhausted = 4,
    WriteFailed = 5,
    ProbeFailed = 6,
    InitializationFailed = 7,
}

public enum PerformanceProfileCommand
{
    None = 0,
    Start = 1,
    Stop = 2,
}

public enum PerformanceProfileCommandResult
{
    Accepted = 0,
    Unavailable = 1,
    CommandPending = 2,
    InvalidState = 3,
}

public readonly struct PerformanceProfileControlStatus : IEquatable<PerformanceProfileControlStatus>
{
    public PerformanceProfileControlStatus(
        PerformanceProfileControlState state,
        TimeSpan duration,
        long writtenRecords,
        long bytesWritten,
        PerformanceProfileResult result,
        string artifactName)
    {
        if (state < PerformanceProfileControlState.Unavailable || state > PerformanceProfileControlState.Faulted)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (result < PerformanceProfileResult.None || result > PerformanceProfileResult.InitializationFailed)
            throw new ArgumentOutOfRangeException(nameof(result));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (writtenRecords < 0) throw new ArgumentOutOfRangeException(nameof(writtenRecords));
        if (bytesWritten < 0) throw new ArgumentOutOfRangeException(nameof(bytesWritten));

        artifactName ??= string.Empty;
        if (artifactName.Length != 0 && !IsSafeArtifactName(artifactName))
            throw new ArgumentException(
                "The profile artifact name must be a bounded machine-neutral basename.",
                nameof(artifactName));

        var inactive = state is PerformanceProfileControlState.Unavailable or PerformanceProfileControlState.Idle;
        var active = state is PerformanceProfileControlState.Recording or PerformanceProfileControlState.Stopping;
        var initializationFailed = state == PerformanceProfileControlState.Faulted &&
            result == PerformanceProfileResult.InitializationFailed;
        if (inactive && (duration != TimeSpan.Zero || writtenRecords != 0 || bytesWritten != 0 ||
                result != PerformanceProfileResult.None || artifactName.Length != 0) ||
            active && (result != PerformanceProfileResult.None || artifactName.Length == 0) ||
            state == PerformanceProfileControlState.Complete &&
                (result != PerformanceProfileResult.UserStopped || artifactName.Length == 0) ||
            state == PerformanceProfileControlState.Faulted &&
                (result is PerformanceProfileResult.None or PerformanceProfileResult.UserStopped ||
                    !initializationFailed && artifactName.Length == 0))
        {
            throw new ArgumentException("The performance-profile status fields are inconsistent.", nameof(state));
        }

        State = state;
        Duration = duration;
        WrittenRecords = writtenRecords;
        BytesWritten = bytesWritten;
        Result = result;
        ArtifactName = artifactName;
    }

    public static PerformanceProfileControlStatus Unavailable =>
        CreateInactive(PerformanceProfileControlState.Unavailable);

    public static PerformanceProfileControlStatus Idle =>
        CreateInactive(PerformanceProfileControlState.Idle);

    public PerformanceProfileControlState State { get; }
    public TimeSpan Duration { get; }
    public long WrittenRecords { get; }
    public long BytesWritten { get; }
    public PerformanceProfileResult Result { get; }
    public string ArtifactName { get; }

    public bool Equals(PerformanceProfileControlStatus other) =>
        State == other.State && Duration == other.Duration &&
        WrittenRecords == other.WrittenRecords && BytesWritten == other.BytesWritten &&
        Result == other.Result && string.Equals(ArtifactName, other.ArtifactName, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is PerformanceProfileControlStatus other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(State, Duration, WrittenRecords, BytesWritten, Result, ArtifactName);

    public static bool operator ==(
        PerformanceProfileControlStatus left,
        PerformanceProfileControlStatus right) => left.Equals(right);

    public static bool operator !=(
        PerformanceProfileControlStatus left,
        PerformanceProfileControlStatus right) => !left.Equals(right);

    private static bool IsSafeArtifactName(string value)
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

    private static PerformanceProfileControlStatus CreateInactive(PerformanceProfileControlState state) =>
        new(state, TimeSpan.Zero, 0, 0, PerformanceProfileResult.None, string.Empty);
}

public interface IPerformanceProfileControl
{
    PerformanceProfileControlStatus Status { get; }
    PerformanceProfileCommand PendingCommand { get; }
    long Revision { get; }
    PerformanceProfileCommandResult RequestStart();
    PerformanceProfileCommandResult RequestStop();
}
#endif
