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

    internal void ObservePublications(
        ServiceCycleSemanticRuntimeTraceMultiplexer? trace,
        IServiceCycleDecisionJournalObserver? journal,
        MonotonicTimestamp observedAt,
        bool includeJournal)
    {
        if (trace is null && (!includeJournal || journal is null)) return;
        for (var ordinal = 0; ordinal < _serviceCapacity; ordinal++)
        {
            var slot = _registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var configuration = slot.LatestConfiguration;
            var strategy = slot.LatestStrategy;
            trace?.ObservePublications(ordinal, configuration, strategy, observedAt);
            if (includeJournal)
                journal?.ObservePublications(ordinal, configuration, strategy, observedAt);
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
