using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal struct DecisionJournalPendingDecision
{
    internal DecisionJournalPendingDecision(
        ServiceCycleIdentity cycle,
        MonotonicTimestamp firstObservedAt,
        int startDecisionCode,
        int captureDecisionCode,
        bool queued,
        WakePolicy wake,
        ServiceFault fault)
    {
        Cycle = cycle;
        FirstObservedAt = firstObservedAt;
        StartDecisionCode = startDecisionCode;
        CaptureDecisionCode = captureDecisionCode;
        Queued = queued;
        HasWake = true;
        Wake = wake;
        HasProjection = false;
        Projection = default;
        Fault = fault;
    }

    internal ServiceCycleIdentity Cycle;
    internal MonotonicTimestamp FirstObservedAt;
    internal int StartDecisionCode;
    internal int CaptureDecisionCode;
    internal bool Queued;
    internal bool HasWake;
    internal WakePolicy Wake;
    internal bool HasProjection;
    internal ServiceStateProjectionSnapshot Projection;
    internal ServiceFault Fault;

    internal void SetOutcome(
        WakePolicy wake,
        bool hasWake,
        ServiceStateProjectionSnapshot projection,
        bool hasProjection,
        ServiceFault fault)
    {
        HasWake = hasWake;
        Wake = wake;
        HasProjection = hasProjection;
        Projection = projection;
        Fault = fault;
    }

    internal DecisionJournalObservation ToObservation(
        ServiceCycleTraceServiceId service,
        MonotonicTimestamp observedAt,
        in BatchReceipt terminal) => new(
            service,
            Cycle.Lifecycle.Value,
            Cycle.Config.Value,
            Cycle.Strategy.Value,
            Cycle.Capture.Value,
            Cycle.Cycle.Value,
            FirstObservedAt,
            observedAt,
            StartDecisionCode,
            CaptureDecisionCode,
            HasWake,
            Wake,
            HasProjection,
            in Projection,
            in Fault,
            in terminal);
}
