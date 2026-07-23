using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly struct ServiceCycleTraceSessionId : IEquatable<ServiceCycleTraceSessionId>
{
    public ServiceCycleTraceSessionId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public bool Equals(ServiceCycleTraceSessionId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceSessionId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(ServiceCycleTraceSessionId left, ServiceCycleTraceSessionId right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceSessionId left, ServiceCycleTraceSessionId right) => !left.Equals(right);
}

public readonly struct ServiceCycleTraceServiceId : IEquatable<ServiceCycleTraceServiceId>
{
    public ServiceCycleTraceServiceId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public bool Equals(ServiceCycleTraceServiceId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceServiceId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(ServiceCycleTraceServiceId left, ServiceCycleTraceServiceId right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceServiceId left, ServiceCycleTraceServiceId right) => !left.Equals(right);
}

public readonly struct ServiceCycleTraceEventId : IEquatable<ServiceCycleTraceEventId>
{
    public const ulong MaximumSequence = ulong.MaxValue - 1;

    public ServiceCycleTraceEventId(ServiceCycleTraceSessionId session, ulong sequence)
    {
        if (!session.IsValid) throw new ArgumentException("A valid trace session is required.", nameof(session));
        if (sequence == 0 || sequence > MaximumSequence) throw new ArgumentOutOfRangeException(nameof(sequence));
        Session = session;
        Sequence = sequence;
    }

    private ServiceCycleTraceEventId(ServiceCycleTraceSessionId session, ulong sequence, bool uncheckedBoundary)
    {
        Session = session;
        Sequence = sequence;
    }

    internal static ServiceCycleTraceEventId UncheckedForValidationTests(
        ServiceCycleTraceSessionId session,
        ulong sequence) => new(session, sequence, true);

    public ServiceCycleTraceSessionId Session { get; }
    public ulong Sequence { get; }
    public bool IsValid => Session.IsValid && Sequence is > 0 and <= MaximumSequence;
    public bool Equals(ServiceCycleTraceEventId other) => Session == other.Session && Sequence == other.Sequence;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceEventId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Session, Sequence);
    public static bool operator ==(ServiceCycleTraceEventId left, ServiceCycleTraceEventId right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceEventId left, ServiceCycleTraceEventId right) => !left.Equals(right);
}

public readonly struct ServiceCycleTraceCycleIdentity : IEquatable<ServiceCycleTraceCycleIdentity>
{
    public ServiceCycleTraceCycleIdentity(
        ServiceCycleTraceServiceId service,
        ulong lifecycleGeneration,
        ulong configurationGeneration,
        ulong strategyGeneration,
        ulong captureSequence,
        ulong cycleId)
    {
        if (!service.IsValid) throw new ArgumentException("A valid trace service is required.", nameof(service));
        if (lifecycleGeneration == 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (configurationGeneration == 0) throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        if (strategyGeneration == 0) throw new ArgumentOutOfRangeException(nameof(strategyGeneration));
        if (captureSequence == 0) throw new ArgumentOutOfRangeException(nameof(captureSequence));
        if (cycleId == 0) throw new ArgumentOutOfRangeException(nameof(cycleId));
        Service = service;
        LifecycleGeneration = lifecycleGeneration;
        ConfigurationGeneration = configurationGeneration;
        StrategyGeneration = strategyGeneration;
        CaptureSequence = captureSequence;
        CycleId = cycleId;
    }

    public ServiceCycleTraceServiceId Service { get; }
    public ulong LifecycleGeneration { get; }
    public ulong ConfigurationGeneration { get; }
    public ulong StrategyGeneration { get; }
    public ulong CaptureSequence { get; }
    public ulong CycleId { get; }
    public bool IsValid => Service.IsValid && LifecycleGeneration != 0 && ConfigurationGeneration != 0 &&
        StrategyGeneration != 0 && CaptureSequence != 0 && CycleId != 0;
    public bool Equals(ServiceCycleTraceCycleIdentity other) =>
        Service == other.Service && LifecycleGeneration == other.LifecycleGeneration &&
        ConfigurationGeneration == other.ConfigurationGeneration && StrategyGeneration == other.StrategyGeneration &&
        CaptureSequence == other.CaptureSequence && CycleId == other.CycleId;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceCycleIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        Service, LifecycleGeneration, ConfigurationGeneration, StrategyGeneration, CaptureSequence, CycleId);
    public static bool operator ==(ServiceCycleTraceCycleIdentity left, ServiceCycleTraceCycleIdentity right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceCycleIdentity left, ServiceCycleTraceCycleIdentity right) => !left.Equals(right);
}

/// <summary>
/// Exact identity available while a main-thread capture is in progress. Strategy generation is
/// deliberately absent: it is an output of a successful capture and cannot be invented for a
/// capture that is unavailable or faults.
/// </summary>
public readonly struct ServiceCycleTraceCaptureIdentity : IEquatable<ServiceCycleTraceCaptureIdentity>
{
    public ServiceCycleTraceCaptureIdentity(
        ServiceCycleTraceServiceId service,
        ulong lifecycleGeneration,
        ulong configurationGeneration,
        ulong captureSequence,
        ulong cycleId)
    {
        if (!service.IsValid) throw new ArgumentException("A valid trace service is required.", nameof(service));
        if (lifecycleGeneration == 0) throw new ArgumentOutOfRangeException(nameof(lifecycleGeneration));
        if (configurationGeneration == 0) throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        if (captureSequence == 0) throw new ArgumentOutOfRangeException(nameof(captureSequence));
        if (cycleId == 0) throw new ArgumentOutOfRangeException(nameof(cycleId));
        Service = service;
        LifecycleGeneration = lifecycleGeneration;
        ConfigurationGeneration = configurationGeneration;
        CaptureSequence = captureSequence;
        CycleId = cycleId;
    }

    public ServiceCycleTraceServiceId Service { get; }
    public ulong LifecycleGeneration { get; }
    public ulong ConfigurationGeneration { get; }
    public ulong CaptureSequence { get; }
    public ulong CycleId { get; }
    public bool IsValid => Service.IsValid && LifecycleGeneration != 0 &&
        ConfigurationGeneration != 0 && CaptureSequence != 0 && CycleId != 0;
    public bool Equals(ServiceCycleTraceCaptureIdentity other) =>
        Service == other.Service && LifecycleGeneration == other.LifecycleGeneration &&
        ConfigurationGeneration == other.ConfigurationGeneration &&
        CaptureSequence == other.CaptureSequence && CycleId == other.CycleId;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceCaptureIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        Service, LifecycleGeneration, ConfigurationGeneration, CaptureSequence, CycleId);
    public static bool operator ==(ServiceCycleTraceCaptureIdentity left, ServiceCycleTraceCaptureIdentity right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceCaptureIdentity left, ServiceCycleTraceCaptureIdentity right) => !left.Equals(right);
}

public readonly struct ServiceCycleTraceCursor : IEquatable<ServiceCycleTraceCursor>
{
    internal ServiceCycleTraceCursor(ServiceCycleTraceSessionId session, ulong sequence)
    {
        Session = session;
        Sequence = sequence;
    }

    public ServiceCycleTraceSessionId Session { get; }
    public ulong Sequence { get; }
    public bool IsValid => Session.IsValid;
    public bool Equals(ServiceCycleTraceCursor other) => Session == other.Session && Sequence == other.Sequence;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceCursor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Session, Sequence);
    public static bool operator ==(ServiceCycleTraceCursor left, ServiceCycleTraceCursor right) => left.Equals(right);
    public static bool operator !=(ServiceCycleTraceCursor left, ServiceCycleTraceCursor right) => !left.Equals(right);
}
