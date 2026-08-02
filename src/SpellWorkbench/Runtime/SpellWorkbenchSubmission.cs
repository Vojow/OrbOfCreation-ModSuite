using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum SpellWorkbenchPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    Quarantined = 2,
    WrongThread = 3,
    LifecycleReplaced = 4,
    IdentityUnavailable = 5,
    SelectionUnavailable = 6,
    WrongSelection = 7,
    AlreadyDiscovered = 8,
    DiscoveryUnavailable = 9,
    RecipeUnavailable = 10,
    Unaffordable = 11,
    LoadoutFull = 12,
    CompositionUnsupported = 14,
    MutationPermitUnavailable = 15,
    PostCommitFault = 16,
    VerificationFailed = 17,
}

internal enum SpellWorkbenchNativeStage
{
    None = 0,
    ClearSelection = 1,
    ApplySelection = 2,
    Discover = 3,
    Create = 4,
    Verification = 5,
}

internal readonly struct SpellWorkbenchState
{
    internal SpellWorkbenchState(Guid resolvedRecipeId, bool targetDiscovered,
        Guid[] coreGlyphIds, Guid[] augmentGlyphIds, Guid[] targetSpellInstanceIds)
    {
        ResolvedRecipeId = resolvedRecipeId;
        TargetDiscovered = targetDiscovered;
        CoreGlyphIds = coreGlyphIds ?? Array.Empty<Guid>();
        AugmentGlyphIds = augmentGlyphIds ?? Array.Empty<Guid>();
        TargetSpellInstanceIds = targetSpellInstanceIds ?? Array.Empty<Guid>();
    }

    internal Guid ResolvedRecipeId { get; }
    internal bool TargetDiscovered { get; }
    internal Guid[] CoreGlyphIds { get; }
    internal Guid[] AugmentGlyphIds { get; }
    internal Guid[] TargetSpellInstanceIds { get; }
}

internal readonly struct SpellWorkbenchEvidence
{
    internal SpellWorkbenchEvidence(bool available, in SpellWorkbenchState before, in SpellWorkbenchState after)
    {
        Available = available;
        Before = before;
        After = after;
    }

    internal bool Available { get; }
    internal SpellWorkbenchState Before { get; }
    internal SpellWorkbenchState After { get; }
}

internal readonly struct SpellWorkbenchSubmission
{
    internal SpellWorkbenchSubmission(SpellWorkbenchPreflight preflight,
        SpellWorkbenchNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, in SpellWorkbenchEvidence evidence, string reason)
    {
        if (preflight != SpellWorkbenchPreflight.Proceeded && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A spell workbench failure requires an exact reason.", nameof(reason));
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Evidence = evidence;
        Reason = reason ?? string.Empty;
    }

    internal SpellWorkbenchPreflight Preflight { get; }
    internal SpellWorkbenchNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal SpellWorkbenchEvidence Evidence { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == SpellWorkbenchPreflight.Proceeded && Outcome == NativeMutationOutcome.Verified;

    internal static SpellWorkbenchSubmission Reject(SpellWorkbenchPreflight preflight, string reason) =>
        new(preflight, SpellWorkbenchNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
