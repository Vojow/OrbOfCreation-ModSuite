using OrbModding.Common;

namespace OrbAutomata;

internal enum ReturnToMenuPreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    WrongScene,
    TransitionInProgress,
    ControlUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum ReturnToMenuNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct ReturnToMenuSubmission
{
    internal ReturnToMenuSubmission(
        ReturnToMenuPreflight preflight,
        ReturnToMenuNativeStage stage,
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

    internal ReturnToMenuPreflight Preflight { get; }
    internal ReturnToMenuNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == ReturnToMenuPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ReturnToMenuSubmission Reject(
        ReturnToMenuPreflight preflight,
        string reason) =>
        new(preflight, ReturnToMenuNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
