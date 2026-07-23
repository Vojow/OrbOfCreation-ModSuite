using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleTraceDocument
{
    private readonly ServiceCycleSemanticEvent[] _events;

    internal ServiceCycleTraceDocument(
        ushort schemaVersion,
        ServiceCycleTraceSessionId session,
        ServiceCycleTraceDropRange dropped,
        int serviceCapacity,
        ServiceCycleSemanticEvent[] events)
    {
        SchemaVersion = schemaVersion;
        Session = session;
        Dropped = dropped;
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        ServiceCapacity = serviceCapacity;
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    internal ServiceCycleTraceDocument(
        ushort schemaVersion,
        ServiceCycleTraceSessionId session,
        ServiceCycleTraceDropRange dropped,
        ServiceCycleSemanticEvent[] events)
        : this(schemaVersion, session, dropped, InferServiceCapacity(events), events) { }

    public ushort SchemaVersion { get; }
    public ServiceCycleTraceSessionId Session { get; }
    public ServiceCycleTraceDropRange Dropped { get; }
    public int ServiceCapacity { get; }
    public bool IsComplete => !Dropped.IsPresent;
    public int Count => _events.Length;
    public ServiceCycleSemanticEvent this[int index] => _events[index];
    public ReadOnlySpan<ServiceCycleSemanticEvent> Events => _events;

    private static int InferServiceCapacity(ServiceCycleSemanticEvent[] events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        ulong maximum = 0;
        for (var index = 0; index < events.Length; index++)
            if (events[index].Payload.Service > maximum) maximum = events[index].Payload.Service;
        return maximum == 0 ? 1 : checked((int)maximum);
    }
}
