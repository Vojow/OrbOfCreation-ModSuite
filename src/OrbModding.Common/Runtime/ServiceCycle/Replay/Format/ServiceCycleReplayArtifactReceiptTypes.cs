using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

public readonly struct ServiceCycleReplayArtifactContext
{
    internal ServiceCycleReplayArtifactContext(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayArtifactReceipt previousReceipt,
        long decisionAt)
    {
        Cycle = cycle;
        PreviousReceipt = previousReceipt;
        DecisionAt = decisionAt;
    }

    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayArtifactReceipt PreviousReceipt { get; }
    public long DecisionAt { get; }
}

public readonly struct ServiceCycleReplayArtifactActionResult
{
    internal ServiceCycleReplayArtifactActionResult(
        ServiceActionDisposition disposition,
        int code,
        bool hasNativeEvidence,
        int nativeOutcomeCode,
        long nativeCallsAttempted,
        long mutationAttempts,
        long mutationsCommitted)
    {
        Disposition = disposition;
        Code = code;
        HasNativeEvidence = hasNativeEvidence;
        NativeOutcomeCode = nativeOutcomeCode;
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
    }

    public ServiceActionDisposition Disposition { get; }
    public int Code { get; }
    public bool HasNativeEvidence { get; }
    /// <summary>Zero means absent; otherwise the persisted value is the native outcome enum plus one.</summary>
    public int NativeOutcomeCode { get; }
    public long NativeCallsAttempted { get; }
    public long MutationAttempts { get; }
    public long MutationsCommitted { get; }
}

public readonly struct ServiceCycleReplayArtifactReceipt
{
    internal ServiceCycleReplayArtifactReceipt(
        bool isPresent,
        ServiceCycleReplayCycleKey cycle,
        ulong batch,
        BatchTerminalDisposition disposition,
        int actionCount,
        int committedCount,
        int terminalIndex,
        int untouchedSuffixCount,
        int resultCode,
        ServiceCycleReplayArtifactActionResult terminalAction,
        bool hasTerminalAction,
        long nativeCallsAttempted,
        long mutationAttempts,
        long mutationsCommitted,
        long completedAt,
        long emergencyEpisode,
        long emergencyTransition,
        int emergencyReason)
    {
        IsPresent = isPresent;
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
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
        CompletedAt = completedAt;
        EmergencyEpisode = emergencyEpisode;
        EmergencyTransition = emergencyTransition;
        EmergencyReason = emergencyReason;
    }

    public bool IsPresent { get; }
    public ServiceCycleReplayCycleKey Cycle { get; }
    public ulong Batch { get; }
    public BatchTerminalDisposition Disposition { get; }
    public int ActionCount { get; }
    public int CommittedCount { get; }
    public int TerminalIndex { get; }
    public int UntouchedSuffixCount { get; }
    public int ResultCode { get; }
    public ServiceCycleReplayArtifactActionResult TerminalAction { get; }
    public bool HasTerminalAction { get; }
    public long NativeCallsAttempted { get; }
    public long MutationAttempts { get; }
    public long MutationsCommitted { get; }
    public long CompletedAt { get; }
    public long EmergencyEpisode { get; }
    public long EmergencyTransition { get; }
    public int EmergencyReason { get; }
    public bool HasEmergencyContext => EmergencyEpisode > 0;
}
