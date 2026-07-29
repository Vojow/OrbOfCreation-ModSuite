using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpEvidenceScanner
{
    private readonly ServiceCycleRegistry _registry;
    private readonly int _serviceCapacity;

    internal SuiteFramePumpEvidenceScanner(
        ServiceCycleRegistry registry,
        int serviceCapacity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceCapacity = serviceCapacity;
    }

    /// <summary>
    /// Reports the suite's one configuration record and one strategy bulletin. Read from the registry
    /// rather than from each slot: every slot reads the same publication, so scanning per ordinal
    /// produced the same generation once per registered service.
    /// </summary>
    internal void ObservePublications(
        ServiceCycleSemanticRuntimeTraceMultiplexer? trace,
        IServiceCycleDecisionJournalObserver? journal,
        MonotonicTimestamp observedAt,
        bool includeJournal)
    {
        if (trace is null && (!includeJournal || journal is null)) return;
        var configuration = _registry.Configuration.ReadLatest().Generation;
        var strategy = _registry.Strategy.ReadLatest().Generation;
        trace?.ObservePublications(configuration, strategy, observedAt);
        if (includeJournal)
            journal?.ObservePublications(configuration, strategy, observedAt);
    }

    /// <summary>
    /// Records the services the world freshness gate held. Called only for a frame that deferred
    /// somebody: a slot keeps its last deferral indefinitely, so an unconditional scan would walk
    /// every slot on every frame to rediscover a hold the journal already has.
    /// </summary>
    internal void ObserveWorldGate(
        IServiceCycleDecisionJournalObserver? journal,
        MonotonicTimestamp observedAt)
    {
        if (journal is null) return;
        for (var ordinal = 0; ordinal < _serviceCapacity; ordinal++)
        {
            var deferral = _registry.GetSlot(ordinal).LatestWorldGateDeferral;
            if (!deferral.IsPresent) continue;
            journal.ObserveWorldGate(ordinal, in deferral, observedAt);
        }
    }

    internal void ObserveLifecycle(
        ServiceCycleSemanticRuntimeTraceMultiplexer? trace,
        IServiceCycleDecisionJournalObserver? journal,
        MonotonicTimestamp observedAt)
    {
        if (trace is null && journal is null) return;
        for (var ordinal = 0; ordinal < _serviceCapacity; ordinal++)
        {
            var slot = _registry.GetSlot(ordinal);
            var traceNeeds = trace?.NeedsLifecycleObservation(
                ordinal,
                slot.LifecycleSemanticVersion) == true;
            var journalNeeds = journal?.NeedsLifecycleObservation(
                ordinal,
                slot.LifecycleSemanticVersion) == true;
            if (!traceNeeds && !journalNeeds) continue;
            var snapshot = slot.LifecycleSnapshot;
            if (traceNeeds)
            {
                trace!.ObserveLifecycle(
                    ordinal,
                    in snapshot,
                    slot.LifecycleSemanticVersion,
                    observedAt);
            }
            if (journalNeeds)
            {
                journal!.ObserveLifecycle(
                    ordinal,
                    in snapshot,
                    slot.LifecycleSemanticVersion,
                    observedAt);
            }
        }
    }
}
