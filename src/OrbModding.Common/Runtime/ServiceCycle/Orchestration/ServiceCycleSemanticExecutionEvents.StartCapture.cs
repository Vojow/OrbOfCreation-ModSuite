using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed partial class ServiceCycleSemanticExecutionEvents
{
    internal void StartAttemptObserved(int ordinal, in ServiceCycleStartAttempt attempt)
    {
        var lifecycle = attempt.Cycle.IsValid
            ? attempt.Cycle.Lifecycle
            : _state.For(ordinal).ActiveLifecycle;
        var invocation = attempt.StartInvocation;
        if (invocation.IsPresent && attempt.StartDecisionFact.IsPresent && !attempt.StartDecision.ShouldStart)
        {
            var startContext = invocation.Context;
            var deferredDecision = attempt.StartDecision;
            _recorder.StartDeferred(
                ordinal,
                in startContext,
                in deferredDecision,
                invocation.CompletedAt,
                Duration(invocation.StartedAt, invocation.CompletedAt));
        }
        else if (invocation.IsPresent && attempt.Fault.IsValid && !attempt.CaptureFact.IsPresent)
        {
            var startContext = invocation.Context;
            var fault = attempt.Fault;
            _recorder.StartFaulted(
                ordinal,
                in startContext,
                in fault,
                invocation.CompletedAt,
                Duration(invocation.StartedAt, invocation.CompletedAt),
                attempt.RetryDue);
        }
        if (attempt.CaptureFact.IsPresent)
        {
            var capture = attempt.CaptureFact;
            var context = capture.Context;
            var duration = Duration(capture.StartedAt, capture.CompletedAt);
            if (capture.Fault.IsValid)
            {
                var fault = capture.Fault;
                _recorder.CaptureFaulted(ordinal, in context, in fault, capture.CompletedAt, duration);
            }
            else if (capture.Result.IsCaptured)
            {
                var result = capture.Result;
                _publications.EnsureStrategy(ordinal, result.StrategyGeneration, capture.CompletedAt);
                _recorder.CaptureCompleted(ordinal, in context, in result, capture.CompletedAt, duration);
            }
            else if (capture.Result.IsValid)
            {
                var result = capture.Result;
                _recorder.CaptureUnavailable(ordinal, in context, in result, capture.CompletedAt, duration);
            }
        }

        var recoveredFault = attempt.RecoveredFault;
        var attemptFault = attempt.Fault;
        EmitRecovery(ordinal, lifecycle, in recoveredFault);
        EmitFault(ordinal, lifecycle, in attemptFault, attempt.RetryDue);
        if (!attempt.Queued) return;

        var cycle = attempt.Cycle;
        _publications.EnsureConfiguration(ordinal, cycle.Config, attempt.QueuedAt);
        _publications.EnsureStrategy(ordinal, cycle.Strategy, attempt.QueuedAt);
        var decision = attempt.StartDecision;
        _recorder.CycleQueued(
            ordinal,
            in cycle,
            in decision,
            attempt.QueuedAt,
            attempt.CaptureFact.IsPresent
                ? Duration(attempt.CaptureFact.StartedAt, attempt.QueuedAt)
                : default);
    }

    internal void CaptureStarted(int ordinal, in ServiceCaptureContext context)
    {
        _publications.EnsureConfiguration(ordinal, context.Config, context.CapturedAt);
        _recorder.CaptureStarted(ordinal, in context);
    }
}
