using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed partial class ServiceCycleSemanticExecutionEvents
{
    internal void ActionDispatched(int ordinal, in ServiceActionDispatch dispatch)
    {
        if (!dispatch.Attempted) return;
        var fact = dispatch.ActionFact;
        var context = fact.Context;
        var result = fact.Result;
        _recorder.ActionCompleted(
            ordinal,
            in context,
            in result,
            fact.CompletedAt,
            Duration(fact.StartedAt, fact.CompletedAt));
        var recoveredFault = dispatch.RecoveredFault;
        EmitRecovery(ordinal, context.Cycle.Lifecycle, in recoveredFault);
        if (dispatch.Fault.IsValid)
        {
            var fault = dispatch.Fault;
            var faultedReceipt = dispatch.Receipt;
            EmitTerminalReceipt(ordinal, in faultedReceipt);
            var cycle = context.Cycle;
            _recorder.CycleFaulted(ordinal, in cycle, in fault, fact.CompletedAt, default);
            EmitFault(ordinal, context.Cycle.Lifecycle, in fault, dispatch.RetryDue);
            return;
        }
        var receipt = dispatch.Receipt;
        EmitTerminalReceipt(ordinal, in receipt);
    }

    internal void ActionAttempted(int ordinal, in ServiceActionContext context) =>
        _recorder.ActionAttempted(ordinal, in context);

    internal void EmergencyRejected(int ordinal, in BatchReceipt receipt)
    {
        if (!receipt.IsPresent) return;
        if (receipt.HasTerminalAction)
        {
            var cycle = receipt.Cycle;
            var context = new ServiceActionContext(
                cycle,
                receipt.Batch,
                new ActionId(checked((ulong)receipt.TerminalIndex + 1)),
                receipt.TerminalIndex,
                receipt.CompletedAt);
            var result = receipt.TerminalAction;
            var emergency = receipt.EmergencyStop;
            _recorder.ActionRejectedForEmergency(
                ordinal,
                in context,
                in result,
                in emergency,
                receipt.CompletedAt,
                default);
        }
        EmitTerminalReceipt(ordinal, in receipt);
    }

    internal void EmitTerminalReceipt(int ordinal, in BatchReceipt receipt)
    {
        if (!receipt.IsPresent) return;
        ref var cursor = ref _state.For(ordinal);
        if (IsSameReceipt(in receipt, in cursor.TerminalReceipt)) return;
        _recorder.BatchTerminal(ordinal, in receipt);
        var cycle = receipt.Cycle;
        switch (receipt.Disposition)
        {
            case BatchTerminalDisposition.Completed:
            case BatchTerminalDisposition.Rejected:
                _recorder.CycleCompleted(ordinal, in cycle, receipt.CompletedAt, default);
                break;
            case BatchTerminalDisposition.Orphaned:
                _recorder.CycleOrphaned(ordinal, in cycle, receipt.CompletedAt, default);
                break;
        }
        cursor.TerminalReceipt = receipt;
    }

    internal static bool IsSameReceipt(in BatchReceipt left, in BatchReceipt right) =>
        left.IsPresent == right.IsPresent &&
        (!left.IsPresent || left.Cycle == right.Cycle && left.Batch == right.Batch &&
            left.Disposition == right.Disposition && left.CompletedAt == right.CompletedAt);
}
