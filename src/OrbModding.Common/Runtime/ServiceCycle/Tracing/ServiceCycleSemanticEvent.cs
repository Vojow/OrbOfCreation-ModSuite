using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly struct ServiceCycleSemanticEvent : IEquatable<ServiceCycleSemanticEvent>
{
    internal ServiceCycleSemanticEvent(
        ServiceCycleTraceEventId id,
        ServiceCycleTraceEventId parent,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        if (!id.IsValid) throw new ArgumentException("A valid semantic event identity is required.", nameof(id));
        ServiceCycleSemanticPayloadValidation.EnsureValid(kind, in payload);
        Id = id;
        Parent = parent;
        Kind = kind;
        Payload = payload;
    }

    private ServiceCycleSemanticEvent(
        ServiceCycleTraceEventId id,
        ServiceCycleSemanticEventKind kind,
        ServiceCycleSemanticPayload payload,
        bool uncheckedBoundary)
    {
        Id = id;
        Parent = default;
        Kind = kind;
        Payload = payload;
    }

    private ServiceCycleSemanticEvent(
        ServiceCycleTraceEventId id,
        ServiceCycleTraceEventId parent,
        ServiceCycleSemanticEventKind kind,
        ServiceCycleSemanticPayload payload,
        bool uncheckedBoundary)
    {
        Id = id;
        Parent = parent;
        Kind = kind;
        Payload = payload;
    }

    internal static ServiceCycleSemanticEvent UncheckedForValidationTests(
        ServiceCycleTraceEventId id,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload) => new(id, kind, payload, true);

    internal static ServiceCycleSemanticEvent UncheckedForValidationTests(
        ServiceCycleTraceEventId id,
        ServiceCycleTraceEventId parent,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        return new ServiceCycleSemanticEvent(id, parent, kind, payload, true);
    }

    public ServiceCycleTraceEventId Id { get; }
    public ServiceCycleTraceEventId Parent { get; }
    public ServiceCycleSemanticEventKind Kind { get; }
    public ServiceCycleSemanticPayload Payload { get; }
    public bool HasParent => Parent.IsValid;

    public bool Equals(ServiceCycleSemanticEvent other) =>
        Id == other.Id && Parent == other.Parent && Kind == other.Kind && Payload.Equals(other.Payload);
    public override bool Equals(object? obj) => obj is ServiceCycleSemanticEvent other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Parent, Kind, Payload);
    public static bool operator ==(ServiceCycleSemanticEvent left, ServiceCycleSemanticEvent right) => left.Equals(right);
    public static bool operator !=(ServiceCycleSemanticEvent left, ServiceCycleSemanticEvent right) => !left.Equals(right);
}
