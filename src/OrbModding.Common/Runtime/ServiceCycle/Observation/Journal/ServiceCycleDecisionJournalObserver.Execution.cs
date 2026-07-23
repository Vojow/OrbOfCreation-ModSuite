using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed partial class ServiceCycleDecisionJournalObserver
{
    public void StartAttemptObserved(
        int ordinal,
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !attempt.StartInvocation.IsPresent && !attempt.Queued) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var invocation = attempt.StartInvocation;
            if (invocation.IsPresent)
                EnsureConfiguration(ref service, invocation.Context.LatestConfig, observedAt);
            ObserveFaultTransition(
                ref service,
                attempt.RecoveredFault,
                attempt.Fault,
                observedAt);
            var capture = attempt.CaptureFact;
            if (capture.IsPresent && capture.Result.IsCaptured)
                EnsureStrategy(ref service, capture.Result.StrategyGeneration, observedAt);

            if (service.PendingMatches(attempt.Cycle))
            {
                if (attempt.Queued) service.MarkPendingQueued();
                return;
            }
            if (service.HasPending)
            {
                var prior = service.CompleteWithoutTerminal(observedAt);
                _journal.Observe(in prior);
            }
            if (capture.IsPresent && capture.Result.IsCaptured)
            {
                service.BeginCycle(in attempt, observedAt);
                return;
            }
            if (attempt.Queued)
                throw new InvalidOperationException("A queued journal cycle lost its captured decision.");
            var immediate = service.Immediate(in attempt, observedAt);
            _journal.Observe(in immediate);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void ResponseAcquired(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !acquisition.Acquired) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var response = acquisition.Response;
            ObserveFaultTransition(
                ref service,
                response.RecoveredFault,
                response.Fault,
                observedAt);
            if (service.ApplyResponse(in acquisition, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void ActionDispatched(
        int ordinal,
        in ServiceActionDispatch dispatch,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !dispatch.Attempted) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            ObserveFaultTransition(
                ref service,
                dispatch.RecoveredFault,
                dispatch.Fault,
                observedAt);
            if (!dispatch.BatchTerminal) return;
            if (service.ApplyTerminal(dispatch.Receipt, dispatch.Fault, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }

    public void EmergencyRejected(
        int ordinal,
        in BatchReceipt receipt,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted || !receipt.IsPresent) return;
        try
        {
            ref var service = ref BoundService(ordinal);
            var fault = default(ServiceFault);
            if (service.ApplyTerminal(receipt, fault, observedAt, out var decision))
                _journal.Observe(in decision);
        }
        catch (Exception exception) when (CanContain(exception)) { _faulted = true; }
    }
}
