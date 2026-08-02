using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct CraftingPlayerAction
{
    internal CraftingPlayerAction(Guid recipeId, long lifecycleEpoch)
    {
        if (recipeId == Guid.Empty)
            throw new ArgumentException("A crafting recipe UUID is required.", nameof(recipeId));
        RecipeId = recipeId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid RecipeId { get; }
    internal long LifecycleEpoch { get; }
}

internal enum CraftingPlayerPipeline
{
    None = 0,
    Direct = 1,
    QueueStack = 2,
    QueueNew = 3,
    QueueInstant = 4,
}

internal enum CraftingPlayerPreflight
{
    Proceeded = 0,
    LifecycleReplaced = 1,
    ContractUnavailable = 2,
    Quarantined = 3,
    WrongThread = 4,
    RecipeUnavailable = 5,
    NotVisible = 6,
    PageRelationAmbiguous = 7,
    InvalidPurchaseAmount = 8,
    QueueFull = 9,
    Unaffordable = 10,
    MutationPermitUnavailable = 11,
    PostCommitFault = 12,
    VerificationFailed = 13,
}

internal enum CraftingPlayerNativeStage
{
    None = 0,
    DirectExecute = 1,
    Payment = 2,
    Construction = 3,
    Initiation = 4,
    Admission = 5,
    Verification = 6,
}

internal readonly struct CraftingPlayerState
{
    internal CraftingPlayerState(
        CraftingPlayerPipeline pipeline,
        BigDouble purchaseAmount,
        BigDouble queuedAmount,
        int queueUsed,
        int queueMaximum)
    {
        Pipeline = pipeline;
        PurchaseAmount = purchaseAmount;
        QueuedAmount = queuedAmount;
        QueueUsed = queueUsed;
        QueueMaximum = queueMaximum;
    }

    internal CraftingPlayerPipeline Pipeline { get; }
    internal BigDouble PurchaseAmount { get; }
    internal BigDouble QueuedAmount { get; }
    internal int QueueUsed { get; }
    internal int QueueMaximum { get; }
}

internal readonly struct CraftingPlayerEvidence
{
    internal CraftingPlayerEvidence(
        bool available,
        in CraftingPlayerState before,
        in CraftingPlayerState after)
    {
        Available = available;
        Before = before;
        After = after;
    }

    internal bool Available { get; }
    internal CraftingPlayerState Before { get; }
    internal CraftingPlayerState After { get; }
}

internal readonly struct CraftingPlayerSubmission
{
    internal CraftingPlayerSubmission(
        Guid recipeId,
        CraftingPlayerPreflight preflight,
        CraftingPlayerNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        in CraftingPlayerEvidence evidence,
        string reason)
    {
        RecipeId = recipeId;
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Evidence = evidence;
        Reason = reason ?? string.Empty;
    }

    internal Guid RecipeId { get; }
    internal CraftingPlayerPreflight Preflight { get; }
    internal CraftingPlayerNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal CraftingPlayerEvidence Evidence { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == CraftingPlayerPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static CraftingPlayerSubmission Reject(
        in CraftingPlayerAction action,
        CraftingPlayerPreflight preflight,
        string reason) =>
        new(
            action.RecipeId,
            preflight,
            CraftingPlayerNativeStage.None,
            default,
            default,
            default,
            reason);
}

internal static class CraftingPlayerActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7701);
    internal static readonly ServiceActionResultCode Quarantined = new(7702);
    internal static readonly ServiceActionResultCode WrongThread = new(7703);
    internal static readonly ServiceActionResultCode RecipeUnavailable = new(7704);
    internal static readonly ServiceActionResultCode NotVisible = new(7705);
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(7706);
    internal static readonly ServiceActionResultCode InvalidPurchaseAmount = new(7707);
    internal static readonly ServiceActionResultCode QueueFull = new(7708);
    internal static readonly ServiceActionResultCode Unaffordable = new(7709);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7710);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7711);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7712);
}
