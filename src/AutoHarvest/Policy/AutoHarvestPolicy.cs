using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoHarvestPolicy
{
    public static AutoHarvestPairDecision EvaluatePair(
        AutoHarvestPair pair,
        bool selected,
        in AutoHarvestPairFacts facts)
    {
        var rejection = AutoHarvestRejectionReason.None;
        if (facts.Identity != AutoHarvestEvidenceState.Verified)
            rejection = AutoHarvestRejectionReason.IdentityUnverified;
        else if (pair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            rejection = AutoHarvestRejectionReason.UnsupportedPair;
        else if (!selected)
            rejection = AutoHarvestRejectionReason.NotSelected;
        else
            rejection = EvaluateFacts(
                facts.PlotVisibility,
                facts.ActionAvailability,
                facts.Prerequisites,
                facts.Readiness);
        return new AutoHarvestPairDecision(
            rejection == AutoHarvestRejectionReason.None,
            rejection);
    }

    /// <summary>
    /// The same decision, taken again at the action boundary against the live action queue.
    /// </summary>
    /// <remarks>
    /// The pair facts and the safety verdict are the ones the action carries — the boundary re-judges
    /// them rather than re-reading them, because the snapshot they came from is the same evidence a
    /// second live read would be checked against, and one of those reads wrote to the game. Safety and
    /// the two queue facts are only ever evaluated here. The queue describes a resource every service
    /// competes for and that this service's own actions consume, so an answer read while deciding is
    /// stale by the time it would be acted on. Safety is a structural audit of the build's authored
    /// content — what stands between the suite and a game whose assets changed under an assembly the
    /// hash gate still accepts — and it is drawn on the worker from collected facts. Neither means
    /// anything anywhere but here.
    /// </remarks>
    public static AutoHarvestPairDecision EvaluateSubmission(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety,
        in AutoHarvestSubmissionState queue)
    {
        var decision = EvaluatePair(pair, selected: true, facts);
        if (!decision.ShouldSubmit) return decision;

        var rejection = EvaluateSafety(safety);
        if (rejection != AutoHarvestRejectionReason.None)
            return new AutoHarvestPairDecision(false, rejection);

        rejection = EvaluateEvidence(
            AutoHarvestWorldFacts.ProjectNoDuplicate(queue),
            AutoHarvestRejectionReason.DuplicateStateUnknown,
            AutoHarvestRejectionReason.AlreadyQueuedOrRunning);
        if (rejection == AutoHarvestRejectionReason.None)
        {
            rejection = EvaluateEvidence(
                AutoHarvestWorldFacts.ProjectActionSlotAvailability(queue),
                AutoHarvestRejectionReason.ActionSlotStateUnknown,
                AutoHarvestRejectionReason.NoActionSlot);
        }

        return new AutoHarvestPairDecision(
            rejection == AutoHarvestRejectionReason.None,
            rejection);
    }

    private static AutoHarvestRejectionReason EvaluateFacts(
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        PlotActionPrerequisiteEvidence prerequisites,
        AutoHarvestEvidenceState readiness)
    {
        var rejection = EvaluateEvidence(
            plotVisibility,
            AutoHarvestRejectionReason.PlotVisibilityUnknown,
            AutoHarvestRejectionReason.PlotNotVisible);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluateEvidence(
            actionAvailability,
            AutoHarvestRejectionReason.ActionAvailabilityUnknown,
            AutoHarvestRejectionReason.ActionUnavailable);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluatePrerequisites(prerequisites);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        return EvaluateEvidence(
            readiness,
            AutoHarvestRejectionReason.ReadinessUnknown,
            AutoHarvestRejectionReason.NotReady);
    }

    private static AutoHarvestRejectionReason EvaluatePrerequisites(
        PlotActionPrerequisiteEvidence prerequisites) => prerequisites switch
    {
        PlotActionPrerequisiteEvidence.NativeLatchedTrue or
        PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation =>
            AutoHarvestRejectionReason.None,
        _ => AutoHarvestRejectionReason.PrerequisitesUnknown,
    };

    private static AutoHarvestRejectionReason EvaluateSafety(AutoHarvestActionSafetyState safety) => safety switch
    {
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving => AutoHarvestRejectionReason.None,
        AutoHarvestActionSafetyState.Destructive => AutoHarvestRejectionReason.DestructiveAction,
        AutoHarvestActionSafetyState.ResourceDrain => AutoHarvestRejectionReason.ResourceDrainPresent,
        AutoHarvestActionSafetyState.UnsafeCompletionEffects => AutoHarvestRejectionReason.UnsafeCompletionEffects,
        _ => AutoHarvestRejectionReason.PreservationUnknown,
    };

    private static AutoHarvestRejectionReason EvaluateEvidence(
        AutoHarvestEvidenceState state,
        AutoHarvestRejectionReason unknownReason,
        AutoHarvestRejectionReason rejectedReason) => state switch
    {
        AutoHarvestEvidenceState.Verified => AutoHarvestRejectionReason.None,
        AutoHarvestEvidenceState.Rejected => rejectedReason,
        _ => unknownReason,
    };
}
