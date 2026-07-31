using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class DecisionJournalLifecycleObservation
{
    private const int RequestedCode = 1;
    private const int ActivatedCode = 2;

    private readonly IDecisionJournalObservationSink _journal;

    internal DecisionJournalLifecycleObservation(IDecisionJournalObservationSink journal) => _journal = journal;

    internal void Requested(
        ref DecisionJournalServiceCursor service,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt)
    {
        if (lifecycle.Value <= service.RequestedLifecycle.Value) return;
        service.RequestLifecycle(lifecycle);
        Transition(service.Service, lifecycle.Value, observedAt, RequestedCode);
    }

    internal static bool NeedsObservation(
        in DecisionJournalServiceCursor service,
        long lifecycleSemanticVersion) =>
        service.LifecycleSemanticVersion != lifecycleSemanticVersion;

    internal void Observe(
        ref DecisionJournalServiceCursor service,
        in ServiceLifecycleSlotSnapshot snapshot,
        ConfigGeneration configuration,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        var terminal = snapshot.LatestTerminal;
        if (terminal.IsPresent && terminal.Sequence > service.LifecycleTerminalSequence)
        {
            ObserveTerminal(ref service, in terminal, observedAt);
            service.LifecycleTerminalSequence = terminal.Sequence;
        }

        var deferral = snapshot.LatestConstructionDeferral;
        if (deferral.IsPresent && deferral.Sequence > service.ConstructionDeferralSequence)
        {
            var decision = service.ConstructionDeferred(in deferral, configuration, observedAt);
            _journal.Observe(in decision);
            service.ConstructionDeferralSequence = deferral.Sequence;
        }

        if (service.TryConstructionFault(
                snapshot.ConstructionFault,
                snapshot.DesiredLifecycle,
                configuration,
                observedAt,
                out var faultDecision))
        {
            _journal.Observe(in faultDecision);
        }

        var active = snapshot.ActiveLifecycle;
        if (active.Value != 0 && active != service.ActiveLifecycle)
        {
            service.ActivateLifecycle(active);
            Transition(service.Service, active.Value, observedAt, ActivatedCode);
        }
        service.LifecycleSemanticVersion = lifecycleSemanticVersion;
    }

    private void ObserveTerminal(
        ref DecisionJournalServiceCursor service,
        in ServiceLifecycleTerminalFact terminal,
        MonotonicTimestamp observedAt)
    {
        if (terminal.HasResponse)
        {
            var response = terminal.Response;
            if (response.RecoveredFault.IsPresent)
                _journal.BreakServiceSpan(service.Service, observedAt);
            service.ObserveFaultTransition(response.RecoveredFault, response.Fault);
            var acquisition = new ServiceResponseAcquisition(in response, terminal.Receipt);
            if (service.ApplyResponse(in acquisition, observedAt, out var responseDecision))
                _journal.Observe(in responseDecision);
        }
        else if (terminal.HasReceipt)
        {
            var fault = default(ServiceFault);
            if (service.ApplyTerminal(terminal.Receipt, fault, observedAt, out var terminalDecision))
                _journal.Observe(in terminalDecision);
        }
        if (!service.HasPending) return;
        var incomplete = service.CompleteWithoutTerminal(observedAt);
        _journal.Observe(in incomplete);
    }

    private void Transition(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        MonotonicTimestamp observedAt,
        int code)
    {
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            service,
            lifecycle,
            observedAt,
            code);
        _journal.ObserveTransition(in transition);
    }

}
