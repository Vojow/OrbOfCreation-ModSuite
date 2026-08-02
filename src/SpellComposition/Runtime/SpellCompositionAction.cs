using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum CastingDial
{
    Output = 0,
    Reserve = 1,
}

/// <summary>One global Casting-screen dial change.</summary>
internal readonly struct SpellCompositionAction
{
    internal SpellCompositionAction(CastingDial dial, int value, long lifecycleEpoch)
    {
        if (!Enum.IsDefined(typeof(CastingDial), dial))
            throw new ArgumentOutOfRangeException(nameof(dial));
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Dial = dial;
        Value = value;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal CastingDial Dial { get; }
    internal int Value { get; }
    internal long LifecycleEpoch { get; }
}

internal enum SpellCompositionPreflight
{
    Proceeded = 0,
    LifecycleReplaced = 1,
    ContractUnavailable = 2,
    WrongThread = 4,
    LevelOutOfRange = 6,
    AlreadyInRequestedState = 7,
    MutationPermitUnavailable = 15,
    PostCommitFault = 16,
    VerificationFailed = 17,
}

internal enum SpellCompositionNativeStage
{
    None = 0,
    Dial = 1,
    Verification = 2,
}

internal readonly struct SpellCompositionState
{
    internal SpellCompositionState(CastingDial dial, int current, int maximum)
    {
        Dial = dial;
        Current = current;
        Maximum = maximum;
    }

    internal CastingDial Dial { get; }
    internal int Current { get; }
    internal int Maximum { get; }
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
    internal static readonly ServiceActionResultCode WrongThread = new(7303);
    internal static readonly ServiceActionResultCode LevelOutOfRange = new(7305);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(7306);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7314);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7315);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7316);
}
