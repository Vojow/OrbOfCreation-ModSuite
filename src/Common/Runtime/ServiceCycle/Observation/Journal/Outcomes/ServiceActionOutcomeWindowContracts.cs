using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

public enum ServiceActionOutcomeBoundaryKind
{
    None = 0,
    Waiting = 1,
    Committed = 2,
    Skipped = 3,
    Rejected = 4,
    Faulted = 5,
    LifecycleChanged = 6,
    WorldGateHeld = 7,
    EmergencyStopped = 8,
}

/// <summary>The latest real runtime boundary represented inside one service's rolling window.</summary>
public readonly struct ServiceActionOutcomeBoundary : IEquatable<ServiceActionOutcomeBoundary>
{
    internal ServiceActionOutcomeBoundary(
        ServiceActionOutcomeBoundaryKind kind,
        int code,
        ServiceFaultCategory faultCategory = default)
    {
        if (kind is < ServiceActionOutcomeBoundaryKind.None or
            > ServiceActionOutcomeBoundaryKind.EmergencyStopped)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (code < 0) throw new ArgumentOutOfRangeException(nameof(code));
        if (faultCategory is < 0 or > ServiceFaultCategory.Start)
            throw new ArgumentOutOfRangeException(nameof(faultCategory));
        Kind = kind;
        Code = code;
        FaultCategory = faultCategory;
    }

    public ServiceActionOutcomeBoundaryKind Kind { get; }
    public int Code { get; }
    public ServiceFaultCategory FaultCategory { get; }
    public bool IsPresent => Kind != ServiceActionOutcomeBoundaryKind.None;

    public bool Equals(ServiceActionOutcomeBoundary other) =>
        Kind == other.Kind && Code == other.Code && FaultCategory == other.FaultCategory;

    public override bool Equals(object? obj) =>
        obj is ServiceActionOutcomeBoundary other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Code, FaultCategory);
    public static bool operator ==(
        ServiceActionOutcomeBoundary left,
        ServiceActionOutcomeBoundary right) => left.Equals(right);
    public static bool operator !=(
        ServiceActionOutcomeBoundary left,
        ServiceActionOutcomeBoundary right) => !left.Equals(right);
}

/// <summary>Recent action truth for one registered ServiceCycle service.</summary>
public readonly struct ServiceActionOutcomeSnapshot
{
    internal ServiceActionOutcomeSnapshot(
        ServiceId service,
        ServiceShape shape,
        int observationCount,
        long planned,
        long committed,
        long skipped,
        long rejected,
        long faulted,
        ServiceActionOutcomeBoundary lastBoundary)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (shape is not (ServiceShape.Source or ServiceShape.Ordinary))
            throw new ArgumentOutOfRangeException(nameof(shape));
        if (observationCount < 0 || planned < 0 || committed < 0 || skipped < 0 ||
            rejected < 0 || faulted < 0)
            throw new ArgumentOutOfRangeException(nameof(observationCount));
        Service = service;
        Shape = shape;
        ObservationCount = observationCount;
        Planned = planned;
        Committed = committed;
        Skipped = skipped;
        Rejected = rejected;
        Faulted = faulted;
        LastBoundary = lastBoundary;
    }

    public ServiceId Service { get; }
    public ServiceShape Shape { get; }
    public int ObservationCount { get; }
    public long Planned { get; }
    public long Committed { get; }
    public long Skipped { get; }
    public long Rejected { get; }
    public long Faulted { get; }
    public ServiceActionOutcomeBoundary LastBoundary { get; }
}

public readonly struct ServiceActionOutcomeWindowCopyResult
{
    internal ServiceActionOutcomeWindowCopyResult(int availableCount, int writtenCount, long revision)
    {
        AvailableCount = availableCount;
        WrittenCount = writtenCount;
        Revision = revision;
    }

    public int AvailableCount { get; }
    public int WrittenCount { get; }
    public long Revision { get; }
    public bool IsComplete => AvailableCount == WrittenCount;
}

public interface IServiceActionOutcomeWindowSource
{
    int ServiceCount { get; }
    int WindowCapacityPerService { get; }
    long Revision { get; }
    ServiceActionOutcomeWindowCopyResult CopyTo(Span<ServiceActionOutcomeSnapshot> destination);
}

public static class ServiceActionOutcomeWindowSources
{
    public static IServiceActionOutcomeWindowSource Shared => ServiceActionOutcomeWindowRegistry.Shared;
}

/// <summary>Single-owner publication point for the active suite outcome projection.</summary>
public sealed class ServiceActionOutcomeWindowRegistry : IServiceActionOutcomeWindowSource
{
    private IServiceActionOutcomeWindowSource? _source;
    private long _baseRevision;

    public static ServiceActionOutcomeWindowRegistry Shared { get; } = new();

    public int ServiceCount => _source?.ServiceCount ?? 0;
    public int WindowCapacityPerService => _source?.WindowCapacityPerService ?? 0;
    public long Revision => checked(_baseRevision + (_source?.Revision ?? 0));

    public ServiceActionOutcomeWindowCopyResult CopyTo(
        Span<ServiceActionOutcomeSnapshot> destination)
    {
        var source = _source;
        if (source is null)
            return new ServiceActionOutcomeWindowCopyResult(0, 0, Revision);
        var copied = source.CopyTo(destination);
        return new ServiceActionOutcomeWindowCopyResult(
            copied.AvailableCount,
            copied.WrittenCount,
            Revision);
    }

    internal ServiceActionOutcomeWindowRegistration Register(
        IServiceActionOutcomeWindowSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (_source is not null)
            throw new InvalidOperationException("An action-outcome projection is already registered.");
        _baseRevision = checked(_baseRevision + 1);
        _source = source;
        return new ServiceActionOutcomeWindowRegistration(this, source);
    }

    internal void Remove(IServiceActionOutcomeWindowSource source)
    {
        if (!ReferenceEquals(_source, source)) return;
        _baseRevision = checked(_baseRevision + source.Revision + 1);
        _source = null;
    }
}

internal sealed class ServiceActionOutcomeWindowRegistration : IDisposable
{
    private ServiceActionOutcomeWindowRegistry? _registry;
    private IServiceActionOutcomeWindowSource? _source;

    internal ServiceActionOutcomeWindowRegistration(
        ServiceActionOutcomeWindowRegistry registry,
        IServiceActionOutcomeWindowSource source)
    {
        _registry = registry;
        _source = source;
    }

    public void Dispose()
    {
        var registry = _registry;
        var source = _source;
        _registry = null;
        _source = null;
        if (registry is not null && source is not null) registry.Remove(source);
    }
}
