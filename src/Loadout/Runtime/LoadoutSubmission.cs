using OrbModding.Common;

namespace OrbAutomata;

internal enum LoadoutPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    WrongTargetType,
    AlreadyInRequestedState,
    SwitchBlocked,
    EntryUnavailable,
    SlotOutOfRange,
    SlotEmpty,
    SlotOccupied,
    NameOutOfRange,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum LoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly struct LoadoutSubmission
{
    internal LoadoutSubmission(LoadoutPreflight preflight, LoadoutNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome, string reason)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
    }

    internal LoadoutPreflight Preflight { get; }
    internal LoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == LoadoutPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static LoadoutSubmission Reject(LoadoutPreflight preflight, string reason) =>
        new(preflight, LoadoutNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
