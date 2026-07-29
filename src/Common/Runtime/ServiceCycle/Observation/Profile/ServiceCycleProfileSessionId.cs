#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

public readonly struct ServiceCycleProfileSessionId : IEquatable<ServiceCycleProfileSessionId>
{
    public ServiceCycleProfileSessionId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public bool Equals(ServiceCycleProfileSessionId other) => Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is ServiceCycleProfileSessionId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(
        ServiceCycleProfileSessionId left,
        ServiceCycleProfileSessionId right) => left.Equals(right);
    public static bool operator !=(
        ServiceCycleProfileSessionId left,
        ServiceCycleProfileSessionId right) => !left.Equals(right);
}
#endif
