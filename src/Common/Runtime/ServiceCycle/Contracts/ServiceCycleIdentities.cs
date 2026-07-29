using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public readonly struct ServiceId : IEquatable<ServiceId>, IComparable<ServiceId>
{
    private readonly string? _value;

    public ServiceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A service identity is required.", nameof(value));
        _value = value;
    }

    public string Value => _value ?? string.Empty;
    public bool IsValid => !string.IsNullOrEmpty(_value);
    public int CompareTo(ServiceId other) => string.Compare(_value, other._value, StringComparison.Ordinal);
    public bool Equals(ServiceId other) => string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ServiceId other && Equals(other);
    public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
    public override string ToString() => Value;
    public static bool operator ==(ServiceId left, ServiceId right) => left.Equals(right);
    public static bool operator !=(ServiceId left, ServiceId right) => !left.Equals(right);
}

public readonly struct ConfigGeneration : IEquatable<ConfigGeneration>
{
    public ConfigGeneration(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public ConfigGeneration Next() => new(checked(Value + 1));
    public bool Equals(ConfigGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ConfigGeneration other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(ConfigGeneration left, ConfigGeneration right) => left.Equals(right);
    public static bool operator !=(ConfigGeneration left, ConfigGeneration right) => !left.Equals(right);
}

public readonly struct CaptureSequence : IEquatable<CaptureSequence>
{
    public CaptureSequence(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public CaptureSequence Next() => new(checked(Value + 1));
    public bool Equals(CaptureSequence other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is CaptureSequence other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(CaptureSequence left, CaptureSequence right) => left.Equals(right);
    public static bool operator !=(CaptureSequence left, CaptureSequence right) => !left.Equals(right);
}

public readonly struct CycleId : IEquatable<CycleId>
{
    public CycleId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public CycleId Next() => new(checked(Value + 1));
    public bool Equals(CycleId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is CycleId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(CycleId left, CycleId right) => left.Equals(right);
    public static bool operator !=(CycleId left, CycleId right) => !left.Equals(right);
}

public readonly struct BatchId : IEquatable<BatchId>
{
    public BatchId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public BatchId Next() => new(checked(Value + 1));
    public bool Equals(BatchId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BatchId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(BatchId left, BatchId right) => left.Equals(right);
    public static bool operator !=(BatchId left, BatchId right) => !left.Equals(right);
}

public readonly struct ActionId : IEquatable<ActionId>
{
    public ActionId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public ActionId Next() => new(checked(Value + 1));
    public bool Equals(ActionId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ActionId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(ActionId left, ActionId right) => left.Equals(right);
    public static bool operator !=(ActionId left, ActionId right) => !left.Equals(right);
}

public readonly struct StatePublicationId : IEquatable<StatePublicationId>
{
    public StatePublicationId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public StatePublicationId Next() => new(checked(Value + 1));
    public bool Equals(StatePublicationId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StatePublicationId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public static bool operator ==(StatePublicationId left, StatePublicationId right) => left.Equals(right);
    public static bool operator !=(StatePublicationId left, StatePublicationId right) => !left.Equals(right);
}
