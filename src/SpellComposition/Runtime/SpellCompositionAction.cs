using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum SpellCompositionActionKind
{
    SetOutputLevel = 0,
    SetAugments = 1,
}

internal readonly struct SpellCompositionGlyphStack
{
    internal SpellCompositionGlyphStack(Guid glyphId, int count)
    {
        if (glyphId == Guid.Empty) throw new ArgumentException("A glyph identity is required.", nameof(glyphId));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        GlyphId = glyphId;
        Count = count;
    }

    internal Guid GlyphId { get; }
    internal int Count { get; }
}

internal readonly struct SpellCompositionAction
{
    internal SpellCompositionAction(
        SpellCompositionActionKind kind,
        Guid spellInstanceId,
        int outputLevel,
        SpellCompositionGlyphStack[] augmentGlyphs,
        long lifecycleEpoch)
    {
        if (kind == SpellCompositionActionKind.SetAugments && spellInstanceId == Guid.Empty)
            throw new ArgumentException("A runtime spell identity is required.", nameof(spellInstanceId));
        if (kind == SpellCompositionActionKind.SetOutputLevel && outputLevel <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputLevel));
        Kind = kind;
        SpellInstanceId = spellInstanceId;
        OutputLevel = outputLevel;
        AugmentGlyphs = augmentGlyphs is null
            ? Array.Empty<SpellCompositionGlyphStack>()
            : (SpellCompositionGlyphStack[])augmentGlyphs.Clone();
        LifecycleEpoch = lifecycleEpoch;
    }

    internal SpellCompositionActionKind Kind { get; }
    internal Guid SpellInstanceId { get; }
    internal int OutputLevel { get; }
    internal SpellCompositionGlyphStack[] AugmentGlyphs { get; }
    internal long LifecycleEpoch { get; }
}

internal enum SpellCompositionPreflight
{
    Proceeded = 0,
    LifecycleReplaced = 1,
    ContractUnavailable = 2,
    Quarantined = 3,
    WrongThread = 4,
    IdentityUnavailable = 5,
    OutputLevelOutOfRange = 6,
    AlreadyInRequestedState = 7,
    GlyphIdentityUnavailable = 8,
    DuplicateGlyph = 9,
    GlyphUnavailable = 10,
    NotAnAugment = 11,
    UsageLimitExceeded = 12,
    IncompatibleComposition = 13,
    MasteryRequirementUnmet = 14,
    MutationPermitUnavailable = 15,
    PostCommitFault = 16,
    VerificationFailed = 17,
}

internal enum SpellCompositionNativeStage
{
    None = 0,
    OutputLevel = 1,
    AugmentComposition = 2,
    Verification = 3,
}

internal readonly struct SpellCompositionState
{
    internal SpellCompositionState(
        int outputLevel,
        int maximumOutputLevel,
        Guid spellInstanceId,
        Guid spellRecipeId,
        SpellCompositionGlyphStack[] augmentGlyphs)
    {
        OutputLevel = outputLevel;
        MaximumOutputLevel = maximumOutputLevel;
        SpellInstanceId = spellInstanceId;
        SpellRecipeId = spellRecipeId;
        AugmentGlyphs = augmentGlyphs ?? Array.Empty<SpellCompositionGlyphStack>();
    }

    internal int OutputLevel { get; }
    internal int MaximumOutputLevel { get; }
    internal Guid SpellInstanceId { get; }
    internal Guid SpellRecipeId { get; }
    internal SpellCompositionGlyphStack[] AugmentGlyphs { get; }
}

internal readonly struct SpellCompositionEvidence
{
    internal SpellCompositionEvidence(
        bool available,
        in SpellCompositionState before,
        in SpellCompositionState after)
    {
        Available = available;
        Before = before;
        After = after;
    }

    internal bool Available { get; }
    internal SpellCompositionState Before { get; }
    internal SpellCompositionState After { get; }
}

internal readonly struct SpellCompositionSubmission
{
    internal SpellCompositionSubmission(
        SpellCompositionPreflight preflight,
        SpellCompositionNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        in SpellCompositionEvidence evidence,
        string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Evidence = evidence;
        Reason = reason ?? string.Empty;
    }

    internal SpellCompositionPreflight Preflight { get; }
    internal SpellCompositionNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal SpellCompositionEvidence Evidence { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == SpellCompositionPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static SpellCompositionSubmission Reject(
        SpellCompositionPreflight preflight,
        string reason) =>
        new(
            preflight,
            SpellCompositionNativeStage.None,
            default,
            default,
            default,
            reason);
}

internal static class SpellCompositionActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7301);
    internal static readonly ServiceActionResultCode Quarantined = new(7302);
    internal static readonly ServiceActionResultCode WrongThread = new(7303);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(7304);
    internal static readonly ServiceActionResultCode OutputLevelOutOfRange = new(7305);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(7306);
    internal static readonly ServiceActionResultCode GlyphIdentityUnavailable = new(7307);
    internal static readonly ServiceActionResultCode DuplicateGlyph = new(7308);
    internal static readonly ServiceActionResultCode GlyphUnavailable = new(7309);
    internal static readonly ServiceActionResultCode NotAnAugment = new(7310);
    internal static readonly ServiceActionResultCode UsageLimitExceeded = new(7311);
    internal static readonly ServiceActionResultCode IncompatibleComposition = new(7312);
    internal static readonly ServiceActionResultCode MasteryRequirementUnmet = new(7313);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7314);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7315);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7316);
}
