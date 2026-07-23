using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver : IServiceCycleDecisionJournalObserver
{
    private readonly DecisionJournalCoalescer _journal;
    private readonly DecisionJournalServiceCursor[] _services;
    private readonly DecisionJournalLifecycleObservation _lifecycle;
    private bool _faulted;

    internal ServiceCycleDecisionJournalObserver(
        DecisionJournalCoalescer journal,
        int serviceCapacity)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _services = new DecisionJournalServiceCursor[serviceCapacity];
        _lifecycle = new DecisionJournalLifecycleObservation(journal);
    }

    public bool IsFaulted => _faulted || _journal.IsFaulted;

    public void Bind(
        int ordinal,
        LifecycleGeneration lifecycle,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref Service(ordinal);
            var id = new ServiceCycleTraceServiceId(checked((ulong)ordinal + 1));
            service.Bind(
                id,
                lifecycle,
                configuration,
                strategy,
                fault,
                lifecycleSemanticVersion,
                lifecycleTerminalSequence,
                constructionDeferralSequence);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void ObservePublications(
        int ordinal,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            EnsureConfiguration(ref service, configuration, observedAt);
            EnsureStrategy(ref service, strategy, observedAt);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    private void EnsureConfiguration(
        ref DecisionJournalServiceCursor service,
        ConfigGeneration generation,
        MonotonicTimestamp observedAt)
    {
        if (!generation.IsValid || generation.Value <= service.Configuration.Value) return;
        service.SetConfiguration(generation);
        Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            service.Service,
            generation.Value,
            observedAt);
    }

    private void ObserveFaultTransition(
        ref DecisionJournalServiceCursor service,
        ServiceFaultRecoveryFact recovery,
        ServiceFault fault,
        MonotonicTimestamp observedAt)
    {
        if (recovery.IsPresent)
            _journal.BreakServiceSpan(service.Service, observedAt);
        service.ObserveFaultTransition(recovery, fault);
    }

    private void EnsureStrategy(
        ref DecisionJournalServiceCursor service,
        StrategyGeneration generation,
        MonotonicTimestamp observedAt)
    {
        if (generation.Value == 0 || generation.Value <= service.Strategy.Value) return;
        service.SetStrategy(generation);
        Transition(
            DecisionJournalRecordKind.StrategyChanged,
            service.Service,
            generation.Value,
            observedAt);
    }

    private void Transition(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong generation,
        MonotonicTimestamp observedAt,
        int code = 0)
    {
        var transition = DecisionJournalRecord.Transition(kind, service, generation, observedAt, code);
        _journal.ObserveTransition(in transition);
    }

    private ref DecisionJournalServiceCursor Service(int ordinal)
    {
        if ((uint)ordinal >= (uint)_services.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return ref _services[ordinal];
    }

    private ref DecisionJournalServiceCursor BoundService(int ordinal)
    {
        ref var service = ref Service(ordinal);
        if (!service.IsBound) throw new InvalidOperationException("The journal service is not bound.");
        return ref service;
    }

    private static bool CanContain(Exception exception) =>
        !BufferedSegmentFailurePolicy.IsProcessFatal(exception);
}
