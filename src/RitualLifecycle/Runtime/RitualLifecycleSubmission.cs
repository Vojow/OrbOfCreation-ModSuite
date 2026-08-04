using OrbModding.Common;

namespace OrbAutomata;

internal enum RitualLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotDiscovered,
    AlreadyInRequestedState,
    NotSelected,
    LevelLocked,
    LevelOutOfRange,
    BattleAlreadyActive,
    NoBattleActive,
    WrongActiveRitual,
    Unaffordable,
    NoDurationEffect,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum RitualLifecycleNativeStage
{
    None = 0,
    Payment = 1,
    NativeCallback = 2,
    Verification = 3,
}

internal readonly struct RitualLifecycleSubmission
{
    internal RitualLifecycleSubmission(
        RitualLifecyclePreflight preflight,
        RitualLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal RitualLifecyclePreflight Preflight { get; }
    internal RitualLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == RitualLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static RitualLifecycleSubmission Reject(
        RitualLifecyclePreflight preflight,
        string reason) =>
        new(preflight, RitualLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
