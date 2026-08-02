using OrbModding.Common;

namespace OrbAutomata;

internal enum PrestigePreflight
{
    Proceeded = 0,
    WrongThread = 1,
    LifecycleReplaced = 2,
    ContractUnavailable = 3,
    WorldCycleIncomplete = 5,
    ChallengesNotFetched = 6,
    MutationPermitUnavailable = 7,
    PostCommitFault = 8,
    VerificationFailed = 9,
}

internal enum PrestigeNativeStage
{
    None = 0,
    NativeTransaction = 1,
    Verification = 2,
}

internal readonly struct PrestigeState
{
    internal PrestigeState(bool evidenceAvailable, long lifecycleEpoch,
        bool worldCycleComplete, bool challengesFetched, int resetCount)
    {
        EvidenceAvailable = evidenceAvailable;
        LifecycleEpoch = lifecycleEpoch;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
        ResetCount = resetCount;
    }

    internal bool EvidenceAvailable { get; }
    internal long LifecycleEpoch { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
    internal int ResetCount { get; }
}

internal readonly struct PrestigeReceipt
{
    internal PrestigeReceipt(in PrestigeState before, in PrestigeState after)
    {
        Before = before;
        After = after;
    }
    internal PrestigeState Before { get; }
    internal PrestigeState After { get; }
    internal bool EvidenceAvailable => Before.EvidenceAvailable || After.EvidenceAvailable;
}

internal readonly struct PrestigeSubmission
{
    internal PrestigeSubmission(PrestigePreflight preflight, PrestigeNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        in PrestigeReceipt receipt, string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }

    internal PrestigePreflight Preflight { get; }
    internal PrestigeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal PrestigeReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == PrestigePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static PrestigeSubmission Reject(PrestigePreflight preflight, string reason) =>
        new(preflight, PrestigeNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
