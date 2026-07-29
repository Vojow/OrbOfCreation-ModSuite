using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceBatchActionOutcome<TState, TAction>
{
    private readonly ServiceBatchRuntime<TState, TAction> _runtime;
    private readonly ServiceBatchCompletion<TState, TAction> _completion;

    internal ServiceBatchActionOutcome(
        ServiceBatchRuntime<TState, TAction> runtime,
        ServiceBatchCompletion<TState, TAction> completion)
    {
        _runtime = runtime;
        _completion = completion;
    }

    internal ServiceActionDispatch Advance(
        in ServiceActionFact actionFact,
        in ServiceFaultRecoveryFact pendingRecovery,
        MonotonicTimestamp observedAt,
        bool committed,
        bool nonBlockingHandoff)
    {
        _runtime.Actions.AdvanceCurrentAndClear();
        if (committed) _runtime.State.CommittedCount++;
        _runtime.State.ActionCursor++;
        if (_runtime.Actions.IsComplete)
        {
            var completed = BatchReceipt.Completed(
                _runtime.State.ActiveCycle,
                _runtime.State.ActiveBatch,
                _runtime.Actions.Count,
                _runtime.State.CommittedCount,
                _runtime.State.NativeOutcome,
                observedAt,
                _runtime.State.PublishedCount);
            _completion.CompleteBatch(
                in completed,
                observedAt,
                rejectedOrFaulted: false,
                nonBlockingHandoff);
            return new ServiceActionDispatch(
                actionFact,
                true,
                completed,
                recoveredFault: CommitRecovery(in pendingRecovery));
        }
        if (_runtime.Lifetime.IsSuperseded)
        {
            var orphaned = BatchReceipt.Orphaned(
                _runtime.State.ActiveCycle,
                _runtime.State.ActiveBatch,
                _runtime.Actions.Count,
                _runtime.State.CommittedCount,
                _runtime.Actions.Cursor,
                _runtime.State.NativeOutcome,
                observedAt,
                _runtime.State.PublishedCount);
            _completion.CompleteLifecycleOrphan(in orphaned);
            return new ServiceActionDispatch(
                actionFact,
                true,
                orphaned,
                recoveredFault: CommitRecovery(in pendingRecovery));
        }
        return new ServiceActionDispatch(
            actionFact,
            false,
            default,
            recoveredFault: CommitRecovery(in pendingRecovery));
    }

    internal ServiceActionDispatch Terminate(
        in ServiceActionFact actionFact,
        in ServiceActionResult result,
        in ServiceFaultRecoveryFact pendingRecovery,
        int index,
        MonotonicTimestamp observedAt,
        bool nonBlockingHandoff)
    {
        var terminal = BatchReceipt.Terminated(
            _runtime.State.ActiveCycle,
            _runtime.State.ActiveBatch,
            _runtime.Actions.Count,
            _runtime.State.CommittedCount,
            index,
            result,
            _runtime.State.NativeOutcome,
            observedAt,
            publishedCount: _runtime.State.PublishedCount);
        _completion.CompleteBatch(
            in terminal,
            observedAt,
            rejectedOrFaulted: true,
            nonBlockingHandoff);
        var fault = default(ServiceFault);
        var retryDue = default(MonotonicTimestamp);
        if (result.Disposition == ServiceActionDisposition.Faulted)
        {
            var record = _runtime.ActionFaults.Record(
                ServiceFaultCategory.ActionExecution,
                result.Code,
                observedAt);
            _runtime.State.LatestFault = record.Fault;
            _runtime.State.ScheduleWake(
                record.RetryDue,
                _runtime.State.ActiveCycle.Config,
                invalidatedByConfiguration: false);
            fault = record.Fault;
            retryDue = record.RetryDue;
        }
        return new ServiceActionDispatch(
            actionFact,
            true,
            terminal,
            fault,
            retryDue,
            CommitRecovery(in pendingRecovery));
    }

    private ServiceFaultRecoveryFact CommitRecovery(
        in ServiceFaultRecoveryFact pendingRecovery)
    {
        if (!pendingRecovery.IsPresent) return default;
        _runtime.ActionFaults.Reset();
        if (_runtime.State.LatestFault.Category == ServiceFaultCategory.ActionExecution)
            _runtime.State.LatestFault = default;
        return pendingRecovery;
    }
}
