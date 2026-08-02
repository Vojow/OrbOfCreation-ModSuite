using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum ChallengePreflight
{
    Proceeded = 0,
    WrongThread = 1,
    LifecycleReplaced = 2,
    ContractUnavailable = 3,
    Quarantined = 4,
    IdentityUnavailable = 5,
    OfferUnavailable = 6,
    SelectionFull = 7,
    SelectionRestricted = 8,
    InvalidState = 9,
    FetchUnavailable = 10,
    NoRerolls = 11,
    MutationPermitUnavailable = 12,
    PostCommitFault = 13,
    VerificationFailed = 14,
}

internal enum ChallengeNativeStage
{
    None = 0,
    DecisionCommit = 1,
    NativeCallback = 2,
    Verification = 3,
}

internal readonly struct ChallengeState
{
    internal ChallengeState(bool evidenceAvailable, int targetState, bool selected,
        bool inTimeOffers, bool inPrestigeOffers, bool worldCycleComplete,
        bool challengesFetched, int rerollsLeft, int rerollsMaximum,
        Guid[] timeOffers, Guid[] prestigeOffers,
        bool timeOffersQueued = false, bool prestigeOffersQueued = false)
    {
        EvidenceAvailable = evidenceAvailable;
        TargetState = targetState;
        Selected = selected;
        InTimeOffers = inTimeOffers;
        InPrestigeOffers = inPrestigeOffers;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
        RerollsLeft = rerollsLeft;
        RerollsMaximum = rerollsMaximum;
        TimeOffers = timeOffers is null ? Array.Empty<Guid>() : (Guid[])timeOffers.Clone();
        PrestigeOffers = prestigeOffers is null ? Array.Empty<Guid>() : (Guid[])prestigeOffers.Clone();
        TimeOffersQueued = timeOffersQueued;
        PrestigeOffersQueued = prestigeOffersQueued;
    }

    internal bool EvidenceAvailable { get; }
    internal int TargetState { get; }
    internal bool Selected { get; }
    internal bool InTimeOffers { get; }
    internal bool InPrestigeOffers { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
    internal int RerollsLeft { get; }
    internal int RerollsMaximum { get; }
    internal Guid[] TimeOffers { get; }
    internal Guid[] PrestigeOffers { get; }
    internal bool TimeOffersQueued { get; }
    internal bool PrestigeOffersQueued { get; }
}

internal readonly struct ChallengeReceipt
{
    internal ChallengeReceipt(ChallengeActionKind kind, in ChallengeState before, in ChallengeState after)
    {
        Kind = kind;
        Before = before;
        After = after;
    }

    internal ChallengeActionKind Kind { get; }
    internal ChallengeState Before { get; }
    internal ChallengeState After { get; }
    internal bool EvidenceAvailable => Before.EvidenceAvailable && After.EvidenceAvailable;
}

internal readonly struct ChallengeSubmission
{
    internal ChallengeSubmission(ChallengePreflight preflight, ChallengeNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        in ChallengeReceipt receipt, string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }

    internal ChallengePreflight Preflight { get; }
    internal ChallengeNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal ChallengeReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == ChallengePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ChallengeSubmission Reject(ChallengePreflight preflight, string reason) =>
        new(preflight, ChallengeNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
