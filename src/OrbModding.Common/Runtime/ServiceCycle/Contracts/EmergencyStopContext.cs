using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public readonly struct EmergencyStopEpisodeId : IEquatable<EmergencyStopEpisodeId>
{
    public EmergencyStopEpisodeId(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public long Value { get; }
    public bool IsValid => Value > 0;
    public bool Equals(EmergencyStopEpisodeId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is EmergencyStopEpisodeId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(EmergencyStopEpisodeId left, EmergencyStopEpisodeId right) => left.Equals(right);
    public static bool operator !=(EmergencyStopEpisodeId left, EmergencyStopEpisodeId right) => !left.Equals(right);
}

public readonly struct EmergencyStopTransitionGeneration : IEquatable<EmergencyStopTransitionGeneration>
{
    public EmergencyStopTransitionGeneration(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public long Value { get; }
    public bool IsValid => Value > 0;
    public bool Equals(EmergencyStopTransitionGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is EmergencyStopTransitionGeneration other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(
        EmergencyStopTransitionGeneration left,
        EmergencyStopTransitionGeneration right) => left.Equals(right);
    public static bool operator !=(
        EmergencyStopTransitionGeneration left,
        EmergencyStopTransitionGeneration right) => !left.Equals(right);
}

/// <summary>Exact engagement episode that first caused an action batch to be rejected.</summary>
public readonly struct EmergencyStopContext : IEquatable<EmergencyStopContext>
{
    public EmergencyStopContext(
        EmergencyStopEpisodeId episode,
        EmergencyStopTransitionGeneration transition,
        EmergencyStopReason reason)
    {
        if (!episode.IsValid) throw new ArgumentException("A valid emergency episode is required.", nameof(episode));
        if (!transition.IsValid)
            throw new ArgumentException("A valid emergency transition is required.", nameof(transition));
        if (reason is < EmergencyStopReason.UserRequested or > EmergencyStopReason.SuiteShutdown)
            throw new ArgumentOutOfRangeException(nameof(reason));
        Episode = episode;
        Transition = transition;
        Reason = reason;
    }

    public EmergencyStopEpisodeId Episode { get; }
    public EmergencyStopTransitionGeneration Transition { get; }
    public EmergencyStopReason Reason { get; }
    public bool IsValid => Episode.IsValid && Transition.IsValid &&
        Reason is >= EmergencyStopReason.UserRequested and <= EmergencyStopReason.SuiteShutdown;
    public bool Equals(EmergencyStopContext other) =>
        Episode == other.Episode && Transition == other.Transition && Reason == other.Reason;
    public override bool Equals(object? obj) => obj is EmergencyStopContext other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Episode, Transition, Reason);
    public static bool operator ==(EmergencyStopContext left, EmergencyStopContext right) => left.Equals(right);
    public static bool operator !=(EmergencyStopContext left, EmergencyStopContext right) => !left.Equals(right);
}
