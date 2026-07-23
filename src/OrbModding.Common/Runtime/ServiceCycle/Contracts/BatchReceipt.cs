using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum BatchTerminalDisposition
{
    Completed = 1,
    Rejected = 2,
    Faulted = 3,
    Orphaned = 4,
}

public readonly struct BatchReceipt
{
    private BatchReceipt(
        ServiceCycleIdentity cycle,
        BatchId batch,
        BatchTerminalDisposition disposition,
        int actionCount,
        int committedCount,
        int terminalIndex,
        int untouchedSuffixCount,
        ServiceActionResultCode resultCode,
        ServiceActionResult terminalAction,
        bool hasTerminalAction,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt,
        EmergencyStopContext emergencyStop)
    {
        Cycle = cycle;
        Batch = batch;
        Disposition = disposition;
        ActionCount = actionCount;
        CommittedCount = committedCount;
        TerminalIndex = terminalIndex;
        UntouchedSuffixCount = untouchedSuffixCount;
        ResultCode = resultCode;
        TerminalAction = terminalAction;
        HasTerminalAction = hasTerminalAction;
        NativeCallOutcome = nativeCallOutcome;
        CompletedAt = completedAt;
        EmergencyStop = emergencyStop;
    }

    public ServiceCycleIdentity Cycle { get; }
    public BatchId Batch { get; }
    public BatchTerminalDisposition Disposition { get; }
    public int ActionCount { get; }
    public int CommittedCount { get; }
    public int TerminalIndex { get; }
    public int UntouchedSuffixCount { get; }
    public ServiceActionResultCode ResultCode { get; }
    public ServiceActionResult TerminalAction { get; }
    public bool HasTerminalAction { get; }
    public ServiceNativeCallTotals NativeCallOutcome { get; }
    public MonotonicTimestamp CompletedAt { get; }
    public EmergencyStopContext EmergencyStop { get; }
    public bool HasEmergencyStopContext => EmergencyStop.IsValid;
    public bool IsPresent => Batch.IsValid;

    public static BatchReceipt Completed(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt)
    {
        ValidateBase(cycle, batch, actionCount);
        if (actionCount == 0)
        {
            if (!IsZero(in nativeCallOutcome))
                throw new ArgumentException("An empty batch cannot carry native evidence.", nameof(nativeCallOutcome));
        }
        else if (nativeCallOutcome.MutationAttempts != nativeCallOutcome.MutationsCommitted ||
                 nativeCallOutcome.MutationsCommitted < actionCount)
        {
            throw new ArgumentException(
                "A completed batch requires committed evidence for every action and every mutation attempt.",
                nameof(nativeCallOutcome));
        }
        return new BatchReceipt(
            cycle, batch, BatchTerminalDisposition.Completed, actionCount, actionCount, -1, 0,
            CommonActionResultCodes.Committed, default, false, nativeCallOutcome, completedAt, default);
    }

    public static BatchReceipt Terminated(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        int committedCount,
        int terminalIndex,
        ServiceActionResult terminalAction,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt,
        EmergencyStopContext emergencyStop = default)
    {
        ValidateBase(cycle, batch, actionCount);
        if (actionCount == 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        if (committedCount != terminalIndex)
            throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (terminalIndex < 0 || terminalIndex >= actionCount)
            throw new ArgumentOutOfRangeException(nameof(terminalIndex));
        if (!terminalAction.IsValid || terminalAction.Disposition == ServiceActionDisposition.Committed)
            throw new ArgumentException("A terminal action must be rejected or faulted.", nameof(terminalAction));
        var terminalNative = terminalAction.NativeCallOutcome;
        var minimumCalls = checked((long)committedCount + terminalNative.NativeCallsAttempted);
        var minimumAttempts = checked((long)committedCount + terminalNative.MutationAttempts);
        var prefixAttempts = checked(nativeCallOutcome.MutationAttempts - terminalNative.MutationAttempts);
        if (nativeCallOutcome.NativeCallsAttempted < minimumCalls ||
            nativeCallOutcome.MutationAttempts < minimumAttempts ||
            prefixAttempts < committedCount ||
            nativeCallOutcome.MutationsCommitted != prefixAttempts)
        {
            throw new ArgumentException(
                "Batch native evidence must exactly account for the committed prefix plus terminal action.",
                nameof(nativeCallOutcome));
        }
        var disposition = terminalAction.Disposition == ServiceActionDisposition.Rejected
            ? BatchTerminalDisposition.Rejected
            : BatchTerminalDisposition.Faulted;
        var isEmergency = terminalAction.Code == CommonActionResultCodes.EmergencyStop;
        if (isEmergency != emergencyStop.IsValid)
            throw new ArgumentException(
                "Emergency-stop termination requires exactly one valid emergency context.",
                nameof(emergencyStop));
        return new BatchReceipt(
            cycle, batch, disposition, actionCount, committedCount, terminalIndex, actionCount - terminalIndex - 1,
            terminalAction.Code, terminalAction, true, nativeCallOutcome, completedAt, emergencyStop);
    }

    public static BatchReceipt Orphaned(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        int committedCount,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt)
    {
        ValidateBase(cycle, batch, actionCount);
        if (committedCount < 0 || committedCount > actionCount)
            throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (committedCount == 0)
        {
            if (!IsZero(in nativeCallOutcome))
                throw new ArgumentException("An orphan with no committed prefix cannot carry native evidence.", nameof(nativeCallOutcome));
        }
        else if (nativeCallOutcome.MutationAttempts != nativeCallOutcome.MutationsCommitted ||
                 nativeCallOutcome.MutationsCommitted < committedCount)
        {
            throw new ArgumentException(
                "Orphaned evidence must contain only fully committed prefix attempts.",
                nameof(nativeCallOutcome));
        }
        return new BatchReceipt(
            cycle, batch, BatchTerminalDisposition.Orphaned, actionCount, committedCount, -1,
            actionCount - committedCount, CommonActionResultCodes.LifecycleReplaced,
            default, false, nativeCallOutcome, completedAt, default);
    }

    private static void ValidateBase(ServiceCycleIdentity cycle, BatchId batch, int actionCount)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        if (!batch.IsValid) throw new ArgumentException("A valid batch identity is required.", nameof(batch));
        if (actionCount < 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
    }

    private static bool IsZero(in ServiceNativeCallTotals outcome) =>
        outcome.NativeCallsAttempted == 0 &&
        outcome.MutationAttempts == 0 &&
        outcome.MutationsCommitted == 0;
}
