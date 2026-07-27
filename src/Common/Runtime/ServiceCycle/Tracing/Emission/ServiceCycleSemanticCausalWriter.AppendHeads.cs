namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

internal sealed partial class ServiceCycleSemanticCausalWriter
{
    private ServiceCycleTraceEventId _suiteHead;

    internal ServiceCycleTraceEventId AppendService(
        int ordinal,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload) =>
        AppendService(ordinal, kind, in payload, _serviceHeads[ordinal]);

    internal ServiceCycleTraceEventId AppendServiceRoot(
        int ordinal,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload) =>
        AppendService(ordinal, kind, in payload, default);

    internal ServiceCycleTraceEventId AppendService(
        int ordinal,
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload,
        ServiceCycleTraceEventId explicitParent)
    {
        _identities.ForRegistrationOrdinal(ordinal);
        var id = _ring.Append(kind, in payload, explicitParent);
        _serviceHeads[ordinal] = id;
        return id;
    }

    internal ServiceCycleTraceEventId AppendSuite(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        var id = _ring.Append(kind, in payload, _suiteHead);
        _suiteHead = id;
        return id;
    }
}
