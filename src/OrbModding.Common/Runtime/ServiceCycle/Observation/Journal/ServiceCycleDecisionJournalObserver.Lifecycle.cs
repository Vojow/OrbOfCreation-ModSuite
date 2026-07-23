using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver
{
    public void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            _lifecycle.Requested(ref service, lifecycle, observedAt);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public bool NeedsLifecycleObservation(int ordinal, long lifecycleSemanticVersion)
    {
        if (IsFaulted) return false;
        try
        {
            ref var service = ref BoundService(ordinal);
            return DecisionJournalLifecycleObservation.NeedsObservation(
                in service,
                lifecycleSemanticVersion);
        }
        catch (Exception exception) when (CanContain(exception))
        {
            _faulted = true;
            return false;
        }
    }

    public void ObserveLifecycle(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            _lifecycle.Observe(ref service, in snapshot, lifecycleSemanticVersion, observedAt);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void EmergencyEntered(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            for (var ordinal = 0; ordinal < _services.Length; ordinal++)
            {
                ref var service = ref BoundService(ordinal);
                if (!service.HasUnqueuedPending) continue;
                var decision = service.CompleteWithoutTerminal(observedAt);
                _journal.Observe(in decision);
            }
            Transition(
                DecisionJournalRecordKind.EmergencyEntered,
                default,
                0,
                observedAt,
                (int)emergency.Reason);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void EmergencyCleared(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            Transition(
                DecisionJournalRecordKind.EmergencyCleared,
                default,
                0,
                observedAt,
                (int)emergency.Reason);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }
}
