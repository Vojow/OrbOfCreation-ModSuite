using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal partial struct DecisionJournalServiceCursor
{
    internal bool PendingMatches(ServiceCycleIdentity cycle) =>
        _hasPending && cycle.IsValid && _pending.Cycle == cycle;

    internal void MarkPendingQueued()
    {
        if (!_hasPending) throw new InvalidOperationException("No journal cycle is pending.");
        _pending.Queued = true;
    }

    internal void BeginCycle(
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt)
    {
        if (_hasPending) throw new InvalidOperationException("A journal cycle is already pending.");
        if (!attempt.Cycle.IsValid || !attempt.StartDecisionFact.IsPresent ||
            !attempt.CaptureFact.IsPresent || !attempt.CaptureResult.IsCaptured)
        {
            throw new ArgumentException("A captured journal cycle is required.", nameof(attempt));
        }
        _pending = new DecisionJournalPendingDecision(
            attempt.Cycle,
            observedAt,
            attempt.StartDecision.Code.Value,
            attempt.CaptureResult.Code.Value,
            attempt.Queued,
            attempt.CaptureResult.WakePolicy,
            _faultState);
        _hasPending = true;
    }

    internal DecisionJournalObservation Immediate(
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt)
    {
        var invocation = attempt.StartInvocation;
        if (!invocation.IsPresent)
            throw new ArgumentException("An immediate journal decision requires a start invocation.", nameof(attempt));
        var start = attempt.StartDecisionFact;
        var capture = attempt.CaptureFact;
        var lifecycle = invocation.Context.Lifecycle.Value;
        var configuration = invocation.Context.LatestConfig.Value;
        var strategy = 0UL;
        var captureSequence = 0UL;
        var cycle = 0UL;
        var captureCode = 0;
        var hasWake = false;
        var wake = default(WakePolicy);
        var fault = _faultState;
        if (capture.IsPresent)
        {
            var context = capture.Context;
            lifecycle = context.Lifecycle.Value;
            configuration = context.Config.Value;
            captureSequence = context.Capture.Value;
            cycle = context.Cycle.Value;
            if (capture.Result.IsValid)
            {
                captureCode = capture.Result.Code.Value;
                strategy = capture.Result.StrategyGeneration.Value;
                hasWake = !capture.Result.IsCaptured;
                wake = capture.Result.WakePolicy;
            }
            else if (capture.Fault.IsValid && start.IsPresent)
            {
                hasWake = true;
                wake = start.Decision.WakePolicy;
            }
        }
        else if (start.IsPresent && !start.Decision.ShouldStart)
        {
            hasWake = true;
            wake = start.Decision.WakePolicy;
        }
        else if (start.IsPresent)
        {
            hasWake = true;
            wake = start.Decision.WakePolicy;
        }

        var projection = default(ServiceStateProjectionSnapshot);
        var terminal = default(BatchReceipt);
        return new DecisionJournalObservation(
            Service,
            lifecycle,
            configuration,
            strategy,
            captureSequence,
            cycle,
            observedAt,
            observedAt,
            start.IsPresent ? start.Decision.Code.Value : 0,
            captureCode,
            hasWake,
            wake,
            false,
            in projection,
            in fault,
            in terminal);
    }
}
