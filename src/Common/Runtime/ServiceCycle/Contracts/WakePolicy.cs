using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum WakePolicyKind
{
    Default = 0,
    Immediate = 1,
    AfterDecision = 2,
    AfterBatch = 3,
    At = 4,
    OnPublication = 5,
}

public readonly struct WakePolicy : IEquatable<WakePolicy>
{
    private WakePolicy(WakePolicyKind kind, MonotonicDuration delay, MonotonicTimestamp dueTime)
    {
        Kind = kind;
        Delay = delay;
        DueTime = dueTime;
    }

    public WakePolicyKind Kind { get; }
    public MonotonicDuration Delay { get; }
    public MonotonicTimestamp DueTime { get; }
    public bool IsValid => Kind is WakePolicyKind.Default or
        WakePolicyKind.Immediate or
        WakePolicyKind.AfterDecision or
        WakePolicyKind.AfterBatch or
        WakePolicyKind.At or
        WakePolicyKind.OnPublication;

    public static WakePolicy Default => default;
    public static WakePolicy Immediate => new(WakePolicyKind.Immediate, default, default);
    public static WakePolicy AfterDecision(MonotonicDuration delay) =>
        new(WakePolicyKind.AfterDecision, delay, default);
    public static WakePolicy AfterBatch(MonotonicDuration delay) =>
        new(WakePolicyKind.AfterBatch, delay, default);
    public static WakePolicy At(MonotonicTimestamp dueTime) => new(WakePolicyKind.At, default, dueTime);
    public static WakePolicy OnPublication =>
        new(WakePolicyKind.OnPublication, default, default);

    public bool Equals(WakePolicy other) =>
        Kind == other.Kind && Delay == other.Delay && DueTime == other.DueTime;
    public override bool Equals(object? obj) => obj is WakePolicy other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Kind, Delay, DueTime);
    public static bool operator ==(WakePolicy left, WakePolicy right) => left.Equals(right);
    public static bool operator !=(WakePolicy left, WakePolicy right) => !left.Equals(right);
}
