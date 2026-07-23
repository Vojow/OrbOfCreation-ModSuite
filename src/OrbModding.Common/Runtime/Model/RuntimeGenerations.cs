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

public readonly struct StrategyGeneration : IEquatable<StrategyGeneration>
{
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
