using OrbModding.Common;

namespace OrbAutomata;

internal enum AlchemyLoadoutPreflight
{
    Proceeded = 0, LifecycleReplaced, ContractUnavailable, WrongThread,
    IdentityUnavailable, WrongDomain, NotDiscovered, AlreadyInRequestedState,
    LoadoutFull, UsageUnavailable, MultiBuyUnavailable, DestinationOutOfRange,
    MutationPermitUnavailable, PostCommitFault, VerificationFailed,
}

internal enum AlchemyLoadoutNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly struct AlchemyLoadoutSubmission
{
    internal AlchemyLoadoutSubmission(AlchemyLoadoutPreflight preflight,
        AlchemyLoadoutNativeStage stage, NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome, string reason)
    {
        Preflight = preflight; Stage = stage; Outcome = outcome;
        CallOutcome = callOutcome; Reason = reason ?? string.Empty;
    }

    internal AlchemyLoadoutPreflight Preflight { get; }
    internal AlchemyLoadoutNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == AlchemyLoadoutPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AlchemyLoadoutSubmission Reject(AlchemyLoadoutPreflight preflight, string reason) =>
        new(preflight, AlchemyLoadoutNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, reason);
}
