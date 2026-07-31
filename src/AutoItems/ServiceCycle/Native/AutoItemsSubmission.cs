using System;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoItemsPreflight
{
    Proceeded = 0,
    ContractUnavailable = 1,
    ItemUnavailable = 2,
    FamilyChanged = 3,
    NativeBusy = 4,
    NotVisible = 5,
    CanFireRefused = 6,
    RandomizationUnavailable = 7,
    MutationPermitUnavailable = 8,
    MultiBuyUnavailable = 9,
    Quarantined = 10,
    TargetUnavailable = 11,
    TemporaryDurationChanged = 12,
    TemporaryCostChanged = 13,
    TemporaryEffectPresent = 14,
    TargetingInProgress = 15,
}

internal readonly struct AutoItemsSubmission
{
    internal AutoItemsSubmission(
        AutoItemsPreflight preflight,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        string reason)
    {
        Preflight = preflight;
        Outcome = outcome;
        CallOutcome = callOutcome;
        Reason = reason ?? string.Empty;
        if (preflight != AutoItemsPreflight.Proceeded && string.IsNullOrWhiteSpace(Reason))
            throw new ArgumentException("An Auto Items rejection requires an exact reason.", nameof(reason));
    }

    internal AutoItemsPreflight Preflight { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
    internal string Reason { get; }
    internal bool Verified =>
        Preflight == AutoItemsPreflight.Proceeded &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AutoItemsSubmission Reject(AutoItemsPreflight preflight, string reason) =>
        new(preflight, NativeMutationOutcome.BeforeCaptureFailed, default, reason);
}
