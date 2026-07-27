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
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(LifecycleRequested)); }
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
            Fault(exception, nameof(NeedsLifecycleObservation));
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
            _lifecycle.Observe(
                ref service,
                in snapshot,
                _configuration,
                lifecycleSemanticVersion,
                observedAt);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(ObserveLifecycle)); }
    }

    /// <summary>
    /// Records that the world freshness gate is holding a service closed. The gate is otherwise
    /// silent — a held service attempts nothing, which reads exactly like a service with no work —
    /// so without this the always-on journal shows a stalled suite as an absence of evidence.
    /// </summary>
    public void ObserveWorldGate(
        int ordinal,
        in ServiceWorldGateDeferralFact deferral,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            if (!service.TryOpenWorldGateHold(in deferral, out var code)) return;
            Transition(
                DecisionJournalRecordKind.WorldGateHeld,
                service.Service,
                service.ActiveLifecycle.Value,
                observedAt,
                code);
        }
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(ObserveWorldGate)); }
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
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(EmergencyEntered)); }
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
        catch (Exception exception) when (CanContain(exception)) { Fault(exception, nameof(EmergencyCleared)); }
    }
}
