using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

internal sealed partial class ServiceCycleSemanticCausalWriter
{
    private readonly ServiceCycleEmergencyCausalMap _emergencyCausality;
    private readonly EmergencyStopContext[] _retainedEmergencyContexts;
    private readonly ServiceCycleTraceEventId[] _retainedEmergencyEntries;

    internal void AppendEmergency(
        ServiceCycleSemanticEventKind kind,
        in EmergencyStopContext emergency,
        in ServiceCycleSemanticPayload payload)
    {
        var id = AppendSuite(kind, in payload);
        if (kind == ServiceCycleSemanticEventKind.EmergencyEntered)
            _emergencyCausality.Record(in emergency, id);
        for (var ordinal = 0; ordinal < _serviceHeads.Length; ordinal++)
            _serviceHeads[ordinal] = id;
    }

    internal void RetainEmergency(int ordinal, in EmergencyStopContext emergency)
    {
        _identities.ForRegistrationOrdinal(ordinal);
        if (_retainedEmergencyContexts[ordinal].IsValid) return;
        if (!_emergencyCausality.TryResolve(in emergency, out var entry))
            throw new InvalidOperationException("The emergency entry is unavailable for causal retention.");
        _retainedEmergencyContexts[ordinal] = emergency;
        _retainedEmergencyEntries[ordinal] = entry;
    }

    internal bool TryResolveEmergency(
        int ordinal,
        in EmergencyStopContext emergency,
        out ServiceCycleTraceEventId entry)
    {
        if (_retainedEmergencyContexts[ordinal] == emergency)
        {
            entry = _retainedEmergencyEntries[ordinal];
            return entry.IsValid;
        }
        return _emergencyCausality.TryResolve(in emergency, out entry);
    }

    internal void ClearRetainedEmergency(int ordinal)
    {
        _identities.ForRegistrationOrdinal(ordinal);
        _retainedEmergencyContexts[ordinal] = default;
        _retainedEmergencyEntries[ordinal] = default;
    }
}
