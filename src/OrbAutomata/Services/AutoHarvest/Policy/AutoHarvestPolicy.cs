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
                facts.Readiness,
                facts.ActionSafety,
                facts.NoDuplicate,
                facts.ActionSlotAvailability);
        return new AutoHarvestPairDecision(
            rejection == AutoHarvestRejectionReason.None,
            rejection);
    }

    private static AutoHarvestRejectionReason EvaluateFacts(
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        AutoHarvestEvidenceState prerequisites,
        AutoHarvestEvidenceState readiness,
        AutoHarvestActionSafetyState actionSafety,
        AutoHarvestEvidenceState noDuplicate,
        AutoHarvestEvidenceState actionSlotAvailability)
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
        rejection = EvaluateEvidence(
            prerequisites,
            AutoHarvestRejectionReason.PrerequisitesUnknown,
            AutoHarvestRejectionReason.PrerequisitesUnmet);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluateEvidence(
            readiness,
            AutoHarvestRejectionReason.ReadinessUnknown,
            AutoHarvestRejectionReason.NotReady);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluateSafety(actionSafety);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluateEvidence(
            noDuplicate,
            AutoHarvestRejectionReason.DuplicateStateUnknown,
            AutoHarvestRejectionReason.AlreadyQueuedOrRunning);
        return rejection != AutoHarvestRejectionReason.None
            ? rejection
            : EvaluateEvidence(
                actionSlotAvailability,
                AutoHarvestRejectionReason.ActionSlotStateUnknown,
                AutoHarvestRejectionReason.NoActionSlot);
    }

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
