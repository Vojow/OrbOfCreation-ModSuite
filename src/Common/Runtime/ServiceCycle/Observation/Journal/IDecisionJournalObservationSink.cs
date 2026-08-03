using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

/// <summary>
/// Consumes the journal's already-assembled decision evidence. Persistence coalesces it; lightweight
/// live projections can consume the same observations without reading or writing an artifact.
/// </summary>
internal interface IDecisionJournalObservationSink
{
    bool IsFaulted { get; }
    void ObserveAction(in DecisionJournalActionObservation observation);
    void Observe(in DecisionJournalObservation observation);
    void ObserveTransition(in DecisionJournalRecord transition);
    void BreakServiceSpan(ServiceCycleTraceServiceId service, MonotonicTimestamp observedAt);
    void Advance(MonotonicTimestamp now);
    void Flush(MonotonicTimestamp now);
    void Stop(MonotonicTimestamp now);
}
