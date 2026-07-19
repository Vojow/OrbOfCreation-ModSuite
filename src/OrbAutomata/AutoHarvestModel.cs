using System;

namespace OrbAutomata;

internal enum AutoHarvestPair
{
    FruitTree,
    TreasureTree,
}

internal static class AutoHarvestKnownIds
{
    public const string FruitTreePlot = "6782dd13-e229-4385-a1aa-8ed86e6ea1ed";
    public const string FruitTreeCollect = "60ea60a2-44e9-41c2-86d6-3935fae0b647";
    public const string TreasureTreePlot = "2d41cfc1-bffa-43b5-b3a8-5e4d5ad85434";
    public const string TreasureTreeCollect = "3eb68f6f-c2f2-405a-88d2-e5c80345aeb4";
    public const string ActivePlotNodeActions = "70871e86-100b-4ae0-ba9b-fc96e09b7e1f";
    public const string CompletionScalingWeight = "be446180-242f-40d2-910e-91e735fc20ad";
    public const string TreasureTreeRewardPool = "1a370ff9-fea7-4a2a-bca7-57fdb2862356";
    public const string FruitTreeRewardPool = "b3ab80f0-80c7-41d4-b4c7-f34c3e909104";

    public static bool IsSupportedPair(string plotUuid, string actionUuid) =>
        Matches(plotUuid, FruitTreePlot) && Matches(actionUuid, FruitTreeCollect) ||
        Matches(plotUuid, TreasureTreePlot) && Matches(actionUuid, TreasureTreeCollect);

    public static bool IsSupportedAction(string actionUuid) =>
        Matches(actionUuid, FruitTreeCollect) || Matches(actionUuid, TreasureTreeCollect);

    private static bool Matches(string actual, string expected) =>
        Guid.TryParse(actual, out var actualUuid) &&
        Guid.TryParse(expected, out var expectedUuid) &&
        actualUuid == expectedUuid;
}

internal enum AutoHarvestObservedPair
{
    Unrelated,
    FruitTree,
    TreasureTree,
    Contradictory,
}

internal static class AutoHarvestIdentityPolicy
{
    public static AutoHarvestObservedPair Classify(
        string plotUuid,
        string actionUuid,
        bool exactFruitReferences,
        bool exactTreasureReferences,
        bool supportedActionReference)
    {
        if (!Guid.TryParse(plotUuid, out _) || !Guid.TryParse(actionUuid, out _))
            return AutoHarvestObservedPair.Contradictory;
        if (exactFruitReferences && !exactTreasureReferences) return AutoHarvestObservedPair.FruitTree;
        if (exactTreasureReferences && !exactFruitReferences) return AutoHarvestObservedPair.TreasureTree;
        if (exactFruitReferences || exactTreasureReferences || supportedActionReference)
            return AutoHarvestObservedPair.Contradictory;
        if (AutoHarvestKnownIds.IsSupportedPair(plotUuid, actionUuid) ||
            AutoHarvestKnownIds.IsSupportedAction(actionUuid))
            return AutoHarvestObservedPair.Contradictory;
        return AutoHarvestObservedPair.Unrelated;
    }
}

internal static class AutoHarvestContractValues
{
    public static bool IsFiniteNear(double actual, double expected, double tolerance = 0.0001) =>
        !double.IsNaN(actual) &&
        !double.IsInfinity(actual) &&
        !double.IsNaN(expected) &&
        !double.IsInfinity(expected) &&
        tolerance >= 0.0 &&
        Math.Abs(actual - expected) <= tolerance;
}

internal enum AutoHarvestEvidenceState
{
    Unknown,
    Rejected,
    Verified,
}

internal enum AutoHarvestActionSafetyState
{
    Unknown,
    Destructive,
    ResourceDrain,
    UnsafeCompletionEffects,
    NativePhaseCyclePreserving,
}

internal enum AutoHarvestRejectionReason
{
    None,
    IdentityUnverified,
    UnsupportedPair,
    NotSelected,
    LifecycleChanged,
    PlotVisibilityUnknown,
    PlotNotVisible,
    ActionAvailabilityUnknown,
    ActionUnavailable,
    PrerequisitesUnknown,
    PrerequisitesUnmet,
    ReadinessUnknown,
    NotReady,
    PreservationUnknown,
    DestructiveAction,
    ResourceDrainPresent,
    UnsafeCompletionEffects,
    DuplicateStateUnknown,
    AlreadyQueuedOrRunning,
    ActionSlotStateUnknown,
    NoActionSlot,
}

internal readonly struct AutoHarvestCandidateSnapshot
{
    public AutoHarvestCandidateSnapshot(
        string plotUuid,
        string actionUuid,
        long lifecycleEpoch,
        bool selected,
        AutoHarvestEvidenceState identity,
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        AutoHarvestEvidenceState prerequisites,
        AutoHarvestEvidenceState readiness,
        AutoHarvestActionSafetyState actionSafety,
        AutoHarvestEvidenceState noDuplicate,
        AutoHarvestEvidenceState actionSlotAvailability)
    {
        PlotUuid = plotUuid ?? string.Empty;
        ActionUuid = actionUuid ?? string.Empty;
        LifecycleEpoch = lifecycleEpoch;
        Selected = selected;
        Identity = identity;
        PlotVisibility = plotVisibility;
        ActionAvailability = actionAvailability;
        Prerequisites = prerequisites;
        Readiness = readiness;
        ActionSafety = actionSafety;
        NoDuplicate = noDuplicate;
        ActionSlotAvailability = actionSlotAvailability;
    }

    public string PlotUuid { get; }
    public string ActionUuid { get; }
    public long LifecycleEpoch { get; }
    public bool Selected { get; }
    public AutoHarvestEvidenceState Identity { get; }
    public AutoHarvestEvidenceState PlotVisibility { get; }
    public AutoHarvestEvidenceState ActionAvailability { get; }
    public AutoHarvestEvidenceState Prerequisites { get; }
    public AutoHarvestEvidenceState Readiness { get; }
    public AutoHarvestActionSafetyState ActionSafety { get; }
    public AutoHarvestEvidenceState NoDuplicate { get; }
    public AutoHarvestEvidenceState ActionSlotAvailability { get; }
}

internal readonly struct AutoHarvestDecision
{
    private AutoHarvestDecision(
        AutoHarvestCandidateSnapshot candidate,
        bool shouldSubmit,
        AutoHarvestRejectionReason rejectionReason)
    {
        Candidate = candidate;
        ShouldSubmit = shouldSubmit;
        RejectionReason = rejectionReason;
    }

    public AutoHarvestCandidateSnapshot Candidate { get; }
    public bool ShouldSubmit { get; }
    public AutoHarvestRejectionReason RejectionReason { get; }

    public static AutoHarvestDecision Submit(AutoHarvestCandidateSnapshot candidate) =>
        new(candidate, true, AutoHarvestRejectionReason.None);

    public static AutoHarvestDecision Reject(
        AutoHarvestCandidateSnapshot candidate,
        AutoHarvestRejectionReason reason) =>
        new(candidate, false, reason);
}

internal static class AutoHarvestPolicy
{
    public static AutoHarvestDecision Evaluate(
        AutoHarvestCandidateSnapshot candidate,
        long currentLifecycleEpoch)
    {
        if (candidate.Identity != AutoHarvestEvidenceState.Verified)
        {
            return Reject(candidate, AutoHarvestRejectionReason.IdentityUnverified);
        }

        if (!AutoHarvestKnownIds.IsSupportedPair(candidate.PlotUuid, candidate.ActionUuid))
        {
            return Reject(candidate, AutoHarvestRejectionReason.UnsupportedPair);
        }

        if (!candidate.Selected)
        {
            return Reject(candidate, AutoHarvestRejectionReason.NotSelected);
        }

        if (candidate.LifecycleEpoch < 0 || candidate.LifecycleEpoch != currentLifecycleEpoch)
        {
            return Reject(candidate, AutoHarvestRejectionReason.LifecycleChanged);
        }

        var evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.PlotVisibility,
            AutoHarvestRejectionReason.PlotVisibilityUnknown,
            AutoHarvestRejectionReason.PlotNotVisible);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.ActionAvailability,
            AutoHarvestRejectionReason.ActionAvailabilityUnknown,
            AutoHarvestRejectionReason.ActionUnavailable);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.Prerequisites,
            AutoHarvestRejectionReason.PrerequisitesUnknown,
            AutoHarvestRejectionReason.PrerequisitesUnmet);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.Readiness,
            AutoHarvestRejectionReason.ReadinessUnknown,
            AutoHarvestRejectionReason.NotReady);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        var safetyRejection = candidate.ActionSafety switch
        {
            AutoHarvestActionSafetyState.NativePhaseCyclePreserving => AutoHarvestRejectionReason.None,
            AutoHarvestActionSafetyState.Destructive => AutoHarvestRejectionReason.DestructiveAction,
            AutoHarvestActionSafetyState.ResourceDrain => AutoHarvestRejectionReason.ResourceDrainPresent,
            AutoHarvestActionSafetyState.UnsafeCompletionEffects => AutoHarvestRejectionReason.UnsafeCompletionEffects,
            _ => AutoHarvestRejectionReason.PreservationUnknown,
        };
        if (safetyRejection != AutoHarvestRejectionReason.None)
        {
            return Reject(candidate, safetyRejection);
        }

        evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.NoDuplicate,
            AutoHarvestRejectionReason.DuplicateStateUnknown,
            AutoHarvestRejectionReason.AlreadyQueuedOrRunning);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        evidenceDecision = EvaluateEvidence(
            candidate,
            candidate.ActionSlotAvailability,
            AutoHarvestRejectionReason.ActionSlotStateUnknown,
            AutoHarvestRejectionReason.NoActionSlot);
        if (evidenceDecision.HasValue)
        {
            return evidenceDecision.Value;
        }

        return AutoHarvestDecision.Submit(candidate);
    }

    private static AutoHarvestDecision? EvaluateEvidence(
        AutoHarvestCandidateSnapshot candidate,
        AutoHarvestEvidenceState state,
        AutoHarvestRejectionReason unknownReason,
        AutoHarvestRejectionReason rejectedReason) =>
        state switch
        {
            AutoHarvestEvidenceState.Verified => null,
            AutoHarvestEvidenceState.Rejected => Reject(candidate, rejectedReason),
            _ => Reject(candidate, unknownReason),
        };

    private static AutoHarvestDecision Reject(
        AutoHarvestCandidateSnapshot candidate,
        AutoHarvestRejectionReason reason) =>
        AutoHarvestDecision.Reject(candidate, reason);
}
