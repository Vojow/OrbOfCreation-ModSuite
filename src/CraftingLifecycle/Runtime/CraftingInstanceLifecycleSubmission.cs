using OrbModding.Common;

namespace OrbAutomata;

internal enum CraftingInstanceLifecyclePreflight
{
    Proceeded = 0,
    LifecycleReplaced,
    ContractUnavailable,
    WrongThread,
    IdentityUnavailable,
    NotVisible,
    PageRelationAmbiguous,
    InstanceUnavailable,
    AutomationFull,
    MultiBuyUnavailable,
    MutationPermitUnavailable,
    PostCommitFault,
    VerificationFailed,
}

internal enum CraftingInstanceLifecycleNativeStage
{
    None = 0,
    NativeCallback = 1,
    Verification = 2,
}

internal readonly struct CraftingInstanceLifecycleSubmission
{
    internal CraftingInstanceLifecycleSubmission(
        CraftingInstanceLifecyclePreflight preflight,
        CraftingInstanceLifecycleNativeStage stage,
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

    internal CraftingInstanceLifecyclePreflight Preflight { get; }
    internal CraftingInstanceLifecycleNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == CraftingInstanceLifecyclePreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static CraftingInstanceLifecycleSubmission Reject(
        CraftingInstanceLifecyclePreflight preflight,
        string reason) =>
        new(preflight, CraftingInstanceLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
