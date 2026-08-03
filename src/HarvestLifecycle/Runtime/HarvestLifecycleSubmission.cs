using OrbModding.Common;

namespace OrbAutomata;

internal enum HarvestLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotVisible,
    ElementListFull,
    ElementUsageUnavailable,
    ActionUnavailable,
    ActionListFull,
    AmountUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum HarvestLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct HarvestLifecycleSubmission
{
    internal HarvestLifecycleSubmission(
        HarvestLifecyclePreflight preflight,
        HarvestLifecycleNativeStage stage,
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

    internal HarvestLifecyclePreflight Preflight { get; }
    internal HarvestLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == HarvestLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static HarvestLifecycleSubmission Reject(
        HarvestLifecyclePreflight preflight,
        string reason) =>
        new(preflight, HarvestLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
