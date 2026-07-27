using System;

namespace OrbModding.Common.Runtime;

public readonly struct LifecycleGeneration : IEquatable<LifecycleGeneration>
{
    public LifecycleGeneration(ulong value) => Value = value;
    public ulong Value { get; }
    public LifecycleGeneration Next() => new(checked(Value + 1));
    public bool Equals(LifecycleGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is LifecycleGeneration other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(LifecycleGeneration left, LifecycleGeneration right) => left.Equals(right);
    public static bool operator !=(LifecycleGeneration left, LifecycleGeneration right) => !left.Equals(right);
}

/// <summary>
/// Stamps one published world snapshot. Its own type, so a world generation can never be compared
/// against a strategy or configuration generation — the mistake that would make a consumer's
/// already-acted-on-this check silently wrong.
/// </summary>
/// <remarks>
/// Zero is rejected, unlike <see cref="StrategyGeneration"/>, so that <c>default</c> reads as "never
/// consumed" rather than as a real generation. Consumers rely on that: a service that must not act
/// twice on the same world compares the published generation against the last one it consumed, and
/// needs a starting value no publication can ever equal.
/// </remarks>
public readonly struct WorldGeneration : IEquatable<WorldGeneration>
{
    public WorldGeneration(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public WorldGeneration Next() => new(checked(Value + 1));
    public bool Equals(WorldGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WorldGeneration other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(WorldGeneration left, WorldGeneration right) => left.Equals(right);
    public static bool operator !=(WorldGeneration left, WorldGeneration right) => !left.Equals(right);
}

public readonly struct StrategyGeneration : IEquatable<StrategyGeneration>
{
    /// <summary>
    /// The neutral bulletin, which is generation one whether or not a strategist ever runs.
    /// </summary>
    /// <remarks>
    /// The registry constructs its publication on the neutral bulletin, so this is what every cycle
    /// is stamped with until a strategist publishes something else — there is no second answer for
    /// "no strategy yet", and the trace identity rejects zero outright.
    /// </remarks>
    public static StrategyGeneration Initial => new(1);

    public StrategyGeneration(ulong value) => Value = value;
    public ulong Value { get; }
    public StrategyGeneration Next() => new(checked(Value + 1));
    public bool Equals(StrategyGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StrategyGeneration other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(StrategyGeneration left, StrategyGeneration right) => left.Equals(right);
    public static bool operator !=(StrategyGeneration left, StrategyGeneration right) => !left.Equals(right);
}
