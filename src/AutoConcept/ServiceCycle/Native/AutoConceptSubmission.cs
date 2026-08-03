using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal enum AutoConceptPreflight
{
    Proceeded = 0,
    ContractUnavailable,
    RecipeIdentityChanged,
    AssignmentUnsettled,
    OwnershipChanged,
    SlotUnavailable,
    ProjectionRefused,
    MasteryLimitChanged,
    ResourceBackpressure,
}

internal readonly struct AutoConceptSubmission
{
    private AutoConceptSubmission(
        AutoConceptPreflight preflight,
        NativeMutationCallOutcome callOutcome,
        NativeMutationOutcome outcome,
        string reason,
        int appliedDelta)
    {
        Preflight = preflight;
        CallOutcome = callOutcome;
        Outcome = outcome;
        Reason = reason;
        AppliedDelta = appliedDelta;
    }

    public AutoConceptPreflight Preflight { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public NativeMutationOutcome Outcome { get; }
    public string Reason { get; }
    public int AppliedDelta { get; }
    public bool Verified =>
        Preflight == AutoConceptPreflight.Proceeded &&
        CallOutcome.MutationAttempts == 1 &&
        Outcome == NativeMutationOutcome.Verified;

    internal static AutoConceptSubmission Rejected(
        AutoConceptPreflight preflight,
        string reason)
    {
        if (preflight == AutoConceptPreflight.Proceeded)
            throw new ArgumentOutOfRangeException(nameof(preflight));
        return new AutoConceptSubmission(preflight, default, default, reason, 0);
    }

    internal static AutoConceptSubmission Attempted(
        NativeMutationCallOutcome outcome,
        NativeMutationOutcome mutationOutcome,
        string reason,
        int appliedDelta) =>
        new(
            AutoConceptPreflight.Proceeded,
            outcome,
            mutationOutcome,
            reason,
            appliedDelta);
}

internal interface IAutoConceptNativePort
{
    AutoConceptSubmission Submit(
        in AutoConceptCycleAction action,
        in AutoConceptConfiguration config);
}
