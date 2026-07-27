using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;

public enum HostTraceDumpState
{
    Unavailable = 0,
    Idle = 1,
    Written = 2,
    Failed = 3,
}

public enum HostTraceDumpRequestResult
{
    Accepted = 0,
    Unavailable = 1,
    RequestPending = 2,
}

/// <summary>What the last dump of the recent-event ring produced.</summary>
public readonly struct HostTraceDumpStatus : IEquatable<HostTraceDumpStatus>
{
    public HostTraceDumpStatus(
        HostTraceDumpState state,
        long writtenEvents,
        long bytesWritten,
        ulong overwrittenEvents,
        string artifactName)
    {
        if (state is < HostTraceDumpState.Unavailable or > HostTraceDumpState.Failed)
            throw new ArgumentOutOfRangeException(nameof(state));
        if (writtenEvents < 0 || bytesWritten < 0)
            throw new ArgumentOutOfRangeException(nameof(writtenEvents));

        artifactName ??= string.Empty;
        if (artifactName.Length != 0 && !IsSafeArtifactName(artifactName))
            throw new ArgumentException(
                "The artifact name must be a bounded machine-neutral basename.",
                nameof(artifactName));

        var written = state == HostTraceDumpState.Written;
        if (!written && (writtenEvents != 0 || bytesWritten != 0 || artifactName.Length != 0) ||
            written && (writtenEvents == 0 || artifactName.Length == 0))
        {
            throw new ArgumentException("The host-trace dump status fields are inconsistent.", nameof(state));
        }

        State = state;
        WrittenEvents = writtenEvents;
        BytesWritten = bytesWritten;
        OverwrittenEvents = overwrittenEvents;
        ArtifactName = artifactName;
    }

    public static HostTraceDumpStatus Unavailable => new(HostTraceDumpState.Unavailable, 0, 0, 0, string.Empty);
    public static HostTraceDumpStatus Idle => new(HostTraceDumpState.Idle, 0, 0, 0, string.Empty);

    public HostTraceDumpState State { get; }
    public long WrittenEvents { get; }
    public long BytesWritten { get; }

    /// <summary>
    /// How many events the ring dropped before this dump. A bounded ring is allowed to lose history;
    /// what it may not do is lose it silently, so the count travels with the artifact.
    /// </summary>
    public ulong OverwrittenEvents { get; }

    public string ArtifactName { get; }

    public bool Equals(HostTraceDumpStatus other) =>
        State == other.State && WrittenEvents == other.WrittenEvents &&
        BytesWritten == other.BytesWritten && OverwrittenEvents == other.OverwrittenEvents &&
        string.Equals(ArtifactName, other.ArtifactName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is HostTraceDumpStatus other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(State, WrittenEvents, BytesWritten, OverwrittenEvents, ArtifactName);

    public static bool operator ==(HostTraceDumpStatus left, HostTraceDumpStatus right) => left.Equals(right);

    public static bool operator !=(HostTraceDumpStatus left, HostTraceDumpStatus right) => !left.Equals(right);

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
}

public interface IHostTraceDumpControl
{
    HostTraceDumpStatus Status { get; }

    bool DumpRequested { get; }

    long Revision { get; }

    HostTraceDumpRequestResult RequestDump();
}
