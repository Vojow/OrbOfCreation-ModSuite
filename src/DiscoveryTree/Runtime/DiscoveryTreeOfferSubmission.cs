using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum DiscoveryTreeOfferPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    TreeUnavailable = 6,
    WrongMode = 7,
    NoDiscoveries = 8,
    OfferUnavailable = 9,
    AlreadyDiscovered = 10,
    RerollUnavailable = 11,
    Unaffordable = 12,
    MutationPermitUnavailable = 13,
    PostCommitFault = 14,
    VerificationFailed = 15,
}

internal enum DiscoveryTreeOfferNativeStage
{
    None = 0,
    Payment = 1,
    Initiate = 2,
    Select = 3,
    Confirm = 4,
    Reroll = 5,
    ClearSelection = 6,
    Verification = 7,
}

internal readonly struct DiscoveryTreeCostReceipt
{
    internal DiscoveryTreeCostReceipt(Guid resourceId, BigDouble expected, BigDouble before, BigDouble after)
    {
        ResourceId = resourceId;
        Expected = expected;
        Before = before;
        After = after;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Expected { get; }
    internal BigDouble Before { get; }
    internal BigDouble After { get; }
    internal BigDouble Charged => Before - After;
}

internal readonly struct DiscoveryTreeOfferState
{
    internal DiscoveryTreeOfferState(
        int mode,
        BigDouble actionTime,
        int rerolls,
        int maximumRerolls,
        bool usedRerollsLastDiscover,
        Guid[] currentChoices,
        Guid[] nextExclusions,
        Guid selectedChoice,
        int totalDiscovered,
        int poolDiscovered,
        bool targetResolved,
        bool targetDiscovered,
        bool targetRequired)
    {
        Mode = mode;
        ActionTime = actionTime;
        Rerolls = rerolls;
        MaximumRerolls = maximumRerolls;
        UsedRerollsLastDiscover = usedRerollsLastDiscover;
        CurrentChoices = currentChoices ?? Array.Empty<Guid>();
        NextExclusions = nextExclusions ?? Array.Empty<Guid>();
        SelectedChoice = selectedChoice;
        TotalDiscovered = totalDiscovered;
        PoolDiscovered = poolDiscovered;
        TargetResolved = targetResolved;
        TargetDiscovered = targetDiscovered;
        TargetRequired = targetRequired;
    }

    internal int Mode { get; }
    internal BigDouble ActionTime { get; }
    internal int Rerolls { get; }
    internal int MaximumRerolls { get; }
    internal bool UsedRerollsLastDiscover { get; }
    internal Guid[] CurrentChoices { get; }
    internal Guid[] NextExclusions { get; }
    internal Guid SelectedChoice { get; }
    internal int TotalDiscovered { get; }
    internal int PoolDiscovered { get; }
    internal bool TargetResolved { get; }
    internal bool TargetDiscovered { get; }
    internal bool TargetRequired { get; }
}

internal readonly struct DiscoveryTreeOfferMutationReceipt
{
    internal DiscoveryTreeOfferMutationReceipt(
        bool evidenceAvailable,
        bool paymentInvoked,
        bool resourcesCharged,
        bool postconditionMatched,
        bool offersPendingNativeIncrement,
        in DiscoveryTreeOfferState before,
        in DiscoveryTreeOfferState after,
        DiscoveryTreeCostReceipt[] costs)
    {
        EvidenceAvailable = evidenceAvailable;
        PaymentInvoked = paymentInvoked;
        ResourcesCharged = resourcesCharged;
        PostconditionMatched = postconditionMatched;
        OffersPendingNativeIncrement = offersPendingNativeIncrement;
        Before = before;
        After = after;
        Costs = costs ?? Array.Empty<DiscoveryTreeCostReceipt>();
    }

    internal bool EvidenceAvailable { get; }
    internal bool PaymentInvoked { get; }
    internal bool ResourcesCharged { get; }
    internal bool PostconditionMatched { get; }
    internal bool OffersPendingNativeIncrement { get; }
    internal DiscoveryTreeOfferState Before { get; }
    internal DiscoveryTreeOfferState After { get; }
    internal DiscoveryTreeCostReceipt[] Costs { get; }
}

internal readonly struct DiscoveryTreeOfferSubmission
{
    internal DiscoveryTreeOfferSubmission(
        DiscoveryTreeOfferPreflight preflight,
        DiscoveryTreeOfferNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        in DiscoveryTreeOfferMutationReceipt receipt,
        string reason)
    {
        if (preflight != DiscoveryTreeOfferPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A Discovery Tree offer failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Receipt = receipt;
        Reason = reason ?? string.Empty;
    }

    internal DiscoveryTreeOfferPreflight Preflight { get; }
    internal DiscoveryTreeOfferNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal DiscoveryTreeOfferMutationReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == DiscoveryTreeOfferPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static DiscoveryTreeOfferSubmission Reject(
        DiscoveryTreeOfferPreflight preflight,
        string reason) =>
        new(preflight, DiscoveryTreeOfferNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
