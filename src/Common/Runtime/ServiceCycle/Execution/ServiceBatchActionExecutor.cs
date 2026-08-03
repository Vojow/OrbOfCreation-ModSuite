using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchActionExecutor<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;
    private readonly ServiceBatchActionOutcome<TState, TAction> _outcomes;

    internal ServiceBatchActionExecutor(
        ServiceBatchRuntime<TState, TAction> runtime,
        ServiceBatchCompletion<TState, TAction> completion)
    {
        _runtime = runtime;
        _outcomes = new ServiceBatchActionOutcome<TState, TAction>(
            runtime,
            completion);
    }

    internal ServiceActionDispatch TryExecuteOne(MonotonicTimestamp now)
    {
#if SERVICE_CYCLE_PROFILE
        var coordinates = default(ServiceCycleProfileCoordinates);
        return TryExecute(
            now,
            nonBlockingHandoff: false,
            ordinal: 0,
            observer: null,
            in coordinates);
#else
        return TryExecute(now, nonBlockingHandoff: false, ordinal: 0, observer: null);
#endif
    }

    internal ServiceActionDispatch TryExecuteOneNonBlocking(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer)
    {
#if SERVICE_CYCLE_PROFILE
        var coordinates = default(ServiceCycleProfileCoordinates);
        return TryExecute(
            now,
            nonBlockingHandoff: true,
            ordinal,
            observer,
            in coordinates);
#else
        return TryExecute(now, nonBlockingHandoff: true, ordinal, observer);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionDispatch TryExecuteOneNonBlockingProfiled(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer,
        in ServiceCycleProfileCoordinates coordinates) =>
        TryExecute(
            now,
            nonBlockingHandoff: true,
            ordinal,
            observer,
            in coordinates);
#endif

    private ServiceActionDispatch TryExecute(
        MonotonicTimestamp now,
        bool nonBlockingHandoff,
        int ordinal,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates coordinates
#endif
        )
    {
        if (!_runtime.State.HasActiveBatch || _runtime.Actions.IsComplete)
            return default;
        var configuration = _runtime.State.CycleConfiguration ??
            throw new InvalidOperationException(
                "The main-owned batch lost its pinned configuration.");
        var index = _runtime.Actions.Cursor;
        var context = new ServiceActionContext(
            _runtime.State.ActiveCycle,
            _runtime.State.ActiveBatch,
            new ActionId(checked((ulong)index + 1)),
            index,
            now
#if SERVICE_CYCLE_PROFILE
            , in coordinates
#endif
            );
        ref readonly var action = ref _runtime.Actions.GetCurrent();
        var snapshot = configuration.Snapshot;
        observer?.ActionAttempted(ordinal, in context);
        var executionStartedAt = _runtime.Clock.Now;
        var attribution = DescribeAction(in action, out var attributionValid);
        var result = attributionValid
            ? ExecuteAction(in action, in snapshot, in context)
            : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);

        var observedAt = _runtime.Clock.Now;
        var actionFact = new ServiceActionFact(
            context,
            result,
            executionStartedAt,
            observedAt);
        _runtime.State.LastAction = actionFact;
        var advances =
            result.Disposition is ServiceActionDisposition.Committed or ServiceActionDisposition.Skipped;
        var pendingRecovery =
            advances
                ? _runtime.ActionFaults.PendingRecovery(observedAt)
                : default;
        var native = result.NativeCallOutcome;
        _runtime.State.NativeOutcome = ServiceActionResult.AddNativeOutcomes(
            in _runtime.State.NativeOutcome,
            in native);
        if (result.Disposition == ServiceActionDisposition.Skipped &&
            result.Effect == ServiceActionEffect.None)
            _runtime.State.PreNativeSkippedCount++;

        return advances
            ? _outcomes.Advance(
                in actionFact,
                in attribution,
                in pendingRecovery,
                observedAt,
                result.Disposition == ServiceActionDisposition.Committed,
                nonBlockingHandoff)
            : _outcomes.Terminate(
                in actionFact,
                in attribution,
                in result,
                in pendingRecovery,
                index,
                observedAt,
                nonBlockingHandoff);
    }

    private ServiceActionResult ExecuteAction(
        in TAction action,
        in SuiteRuntimeConfiguration snapshot,
        in ServiceActionContext context)
    {
        try
        {
            var result = _runtime.Definition.TryExecute(
                in action,
                in snapshot,
                in context);
            return result.IsValid
                ? result
                : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }
        catch
        {
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }
    }

    private ServiceActionJournalAttribution DescribeAction(
        in TAction action,
        out bool valid)
    {
        try
        {
            var attribution = _runtime.Definition.DescribeAction(in action);
            valid = attribution.IsValid;
            return valid ? attribution : ServiceActionJournalAttribution.Failed;
        }
        catch
        {
            valid = false;
            return ServiceActionJournalAttribution.Failed;
        }
    }

}
