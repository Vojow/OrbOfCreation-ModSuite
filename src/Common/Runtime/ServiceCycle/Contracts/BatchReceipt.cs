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
        int publishedCount,
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
        PublishedCount = publishedCount;
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

    /// <summary>
    /// How many committed actions in this batch published a snapshot rather than mutating the game.
    /// </summary>
    /// <remarks>
    /// Native evidence is required per action that could produce it, so the batch has to know how
    /// many of its actions could not. The parameter defaults to zero — meaning "every action was a
    /// native mutation", which is what every caller predating publishing actions meant — and a
    /// caller that forgets it on a publishing batch fails the evidence check rather than slipping
    /// through it.
    /// </remarks>
    public int PublishedCount { get; }

    /// <summary>Actions in this batch that could produce native mutation evidence.</summary>
    public int NativeActionCount => ActionCount - PublishedCount;
    public int SkippedCount => Disposition switch
    {
        BatchTerminalDisposition.Completed => ActionCount - CommittedCount,
        BatchTerminalDisposition.Rejected or BatchTerminalDisposition.Faulted =>
            TerminalIndex - CommittedCount,
        BatchTerminalDisposition.Orphaned =>
            ActionCount - UntouchedSuffixCount - CommittedCount,
        _ => 0,
    };
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
        MonotonicTimestamp completedAt,
        int publishedCount = 0) =>
        Completed(cycle, batch, actionCount, actionCount, nativeCallOutcome, completedAt, publishedCount);

    public static BatchReceipt Completed(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        int committedCount,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt,
        int publishedCount = 0)
    {
        ValidateBase(cycle, batch, actionCount);
        if (committedCount < 0 || committedCount > actionCount)
            throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (publishedCount < 0 || publishedCount > committedCount)
            throw new ArgumentOutOfRangeException(nameof(publishedCount));
        var nativeActions = actionCount - publishedCount;
        var nativeCommitted = committedCount - publishedCount;
        if (nativeActions == 0)
        {
            if (!IsZero(in nativeCallOutcome))
                throw new ArgumentException(
                    "A batch with no native action cannot carry native evidence.",
                    nameof(nativeCallOutcome));
        }
        else if (nativeCallOutcome.MutationAttempts < nativeActions ||
                 nativeCallOutcome.MutationsCommitted < nativeCommitted ||
                 nativeCommitted == 0 && nativeCallOutcome.MutationsCommitted != 0)
        {
            throw new ArgumentException(
                "A completed batch requires one attempted mutation per processed native action and committed evidence for every committed native action.",
                nameof(nativeCallOutcome));
        }
        return new BatchReceipt(
            cycle, batch, BatchTerminalDisposition.Completed, actionCount, committedCount,
            publishedCount, -1, 0,
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
        EmergencyStopContext emergencyStop = default,
        int publishedCount = 0)
    {
        ValidateBase(cycle, batch, actionCount);
        if (actionCount == 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
        if (committedCount < 0 || committedCount > terminalIndex)
            throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (terminalIndex < 0 || terminalIndex >= actionCount)
            throw new ArgumentOutOfRangeException(nameof(terminalIndex));
        if (publishedCount < 0 || publishedCount > committedCount)
            throw new ArgumentOutOfRangeException(nameof(publishedCount));
        if (!terminalAction.IsValid ||
            terminalAction.Disposition is not (
                ServiceActionDisposition.Rejected or ServiceActionDisposition.Faulted))
            throw new ArgumentException("A terminal action must be rejected or faulted.", nameof(terminalAction));
        var terminalNative = terminalAction.NativeCallOutcome;
        var nativePrefix = terminalIndex - publishedCount;
        var minimumCalls = checked((long)nativePrefix + terminalNative.NativeCallsAttempted);
        var minimumAttempts = checked((long)nativePrefix + terminalNative.MutationAttempts);
        var prefixMutations = checked(nativeCallOutcome.MutationsCommitted - terminalNative.MutationsCommitted);
        var nativeCommitted = committedCount - publishedCount;
        if (nativeCallOutcome.NativeCallsAttempted < minimumCalls ||
            nativeCallOutcome.MutationAttempts < minimumAttempts ||
            prefixMutations < nativeCommitted ||
            nativeCommitted == 0 && prefixMutations != 0)
        {
            throw new ArgumentException(
                "Batch native evidence must account for the processed native prefix plus terminal action.",
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
            cycle, batch, disposition, actionCount, committedCount, publishedCount,
            terminalIndex, actionCount - terminalIndex - 1,
            terminalAction.Code, terminalAction, true, nativeCallOutcome, completedAt, emergencyStop);
    }

    public static BatchReceipt Orphaned(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        int committedCount,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt,
        int publishedCount = 0) =>
        Orphaned(
            cycle, batch, actionCount, committedCount, committedCount, nativeCallOutcome, completedAt,
            publishedCount);

    public static BatchReceipt Orphaned(
        ServiceCycleIdentity cycle,
        BatchId batch,
        int actionCount,
        int committedCount,
        int processedCount,
        ServiceNativeCallTotals nativeCallOutcome,
        MonotonicTimestamp completedAt,
        int publishedCount = 0)
    {
        ValidateBase(cycle, batch, actionCount);
        if (processedCount < 0 || processedCount > actionCount)
            throw new ArgumentOutOfRangeException(nameof(processedCount));
        if (committedCount < 0 || committedCount > processedCount)
            throw new ArgumentOutOfRangeException(nameof(committedCount));
        if (publishedCount < 0 || publishedCount > committedCount)
            throw new ArgumentOutOfRangeException(nameof(publishedCount));
        var nativeProcessed = processedCount - publishedCount;
        var nativeCommitted = committedCount - publishedCount;
        if (nativeProcessed == 0)
        {
            if (!IsZero(in nativeCallOutcome))
                throw new ArgumentException(
                    "An orphan with no processed native prefix cannot carry native evidence.",
                    nameof(nativeCallOutcome));
        }
        else if (nativeCallOutcome.MutationAttempts < nativeProcessed ||
                 nativeCallOutcome.MutationsCommitted < nativeCommitted ||
                 nativeCommitted == 0 && nativeCallOutcome.MutationsCommitted != 0)
        {
            throw new ArgumentException(
                "Orphaned evidence must account for every processed native prefix action.",
                nameof(nativeCallOutcome));
        }
        return new BatchReceipt(
            cycle, batch, BatchTerminalDisposition.Orphaned, actionCount, committedCount,
            publishedCount, -1,
            actionCount - processedCount, CommonActionResultCodes.LifecycleReplaced,
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
