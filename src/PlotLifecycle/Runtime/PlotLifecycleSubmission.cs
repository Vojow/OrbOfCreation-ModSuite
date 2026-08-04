using OrbModding.Common;

namespace OrbAutomata;

internal enum PlotLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    PlotUnavailable,
    ActionUnavailable,
    ActionListFull,
    QuantityUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum PlotLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct PlotLifecycleSubmission
{
    internal PlotLifecycleSubmission(
        PlotLifecyclePreflight preflight,
        PlotLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason,
        int beforeQuantity = 0,
        int afterQuantity = 0)
    {
        Preflight = preflight;
        Stage = stage;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        BeforeQuantity = beforeQuantity;
        AfterQuantity = afterQuantity;
    }

    internal PlotLifecyclePreflight Preflight { get; }
    internal PlotLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal int BeforeQuantity { get; }
    internal int AfterQuantity { get; }
    internal bool Verified => Preflight == PlotLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static PlotLifecycleSubmission Reject(
        PlotLifecyclePreflight preflight,
        string reason) =>
        new(preflight, PlotLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
