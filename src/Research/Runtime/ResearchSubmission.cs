using OrbModding.Common;

namespace OrbAutomata;

internal enum ResearchPreflight
{
    Proceeded = 0, WrongThread = 1, LifecycleReplaced = 2, ContractUnavailable = 3,
    Quarantined = 4, IdentityUnavailable = 5, DevelopUnavailable = 6,
    MultiBuyUnavailable = 7, InvalidMode = 8, InvalidState = 9,
    BonusUnavailable = 10, MutationPermitUnavailable = 11, PostCommitFault = 12,
    VerificationFailed = 13,
}

internal enum ResearchNativeStage { None = 0, NativeCallback = 1, Verification = 2 }

internal readonly record struct ResearchState(
    bool EvidenceAvailable, bool QueueMode, int MultiBuy, int Level, int WaitingLevels,
    int QueuedLevels, int Stage, int SelfBonusLevels, bool IsActive, bool IsDeveloping,
    int PurchasedLevels, int BonusLevel, int TotalLevel, int CurrentInvestmentLevel,
    BigDouble TimeRatio, bool CanDevelop, bool WithinDevelopRange,
    bool CanApplyBonusLevel, int FreeBonusLevels, bool CostAffordable, int MaxLevel,
    int LevelsAvailable);

internal readonly record struct ResearchReceipt(
    ResearchActionKind Kind, ResearchState Before, ResearchState After)
{
    internal bool EvidenceAvailable => Before.EvidenceAvailable && After.EvidenceAvailable;
}

internal readonly struct ResearchSubmission
{
    internal ResearchSubmission(ResearchPreflight preflight, ResearchNativeStage stage,
        NativeMutationOutcome outcome, NativeMutationCallOutcome callOutcome,
        in ResearchReceipt receipt, string reason)
    {
        Preflight = preflight; Stage = stage; Outcome = outcome; CallOutcome = callOutcome;
        Receipt = receipt; Reason = reason ?? string.Empty;
    }

    internal ResearchPreflight Preflight { get; }
    internal ResearchNativeStage Stage { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal ResearchReceipt Receipt { get; }
    internal string Reason { get; }
    internal bool Verified => Preflight == ResearchPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static ResearchSubmission Reject(ResearchPreflight preflight, string reason) =>
        new(preflight, ResearchNativeStage.None, NativeMutationOutcome.BeforeCaptureFailed,
            default, default, reason);
}
