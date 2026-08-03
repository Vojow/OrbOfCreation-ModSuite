using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal readonly struct DecisionJournalActionObservation
{
    internal DecisionJournalActionObservation(
        ServiceCycleTraceServiceId service,
        in ServiceActionFact fact,
        in ServiceActionJournalAttribution attribution)
    {
        if (!service.IsValid) throw new ArgumentException("A valid journal service is required.", nameof(service));
        if (!fact.IsPresent) throw new ArgumentException("A dispatched action fact is required.", nameof(fact));
        if (!attribution.IsValid)
            throw new ArgumentException("A valid action attribution is required.", nameof(attribution));
        Service = service;
        Fact = fact;
        Attribution = attribution;
    }

    internal ServiceCycleTraceServiceId Service { get; }
    internal ServiceActionFact Fact { get; }
    internal ServiceActionJournalAttribution Attribution { get; }
}
