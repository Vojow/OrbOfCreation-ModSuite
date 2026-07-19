using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestPolicyTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteSerializedContractValuesNeverMatch(double actual)
    {
        Assert.False(AutoHarvestContractValues.IsFiniteNear(actual, 1.0));
    }

    [Fact]
    public void FiniteSerializedContractValuesRequirePositiveNearEquality()
    {
        Assert.True(AutoHarvestContractValues.IsFiniteNear(3.00005, 3.0));
        Assert.False(AutoHarvestContractValues.IsFiniteNear(3.01, 3.0));
        Assert.False(AutoHarvestContractValues.IsFiniteNear(3.0, 3.0, -0.1));
    }

    [Fact]
    public void SupportedUuidPairOnReplacementReferencesIsContradictory()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Theory]
    [InlineData("", AutoHarvestKnownIds.FruitTreeCollect)]
    [InlineData("not-a-uuid", AutoHarvestKnownIds.FruitTreeCollect)]
    [InlineData(AutoHarvestKnownIds.FruitTreePlot, "")]
    [InlineData(AutoHarvestKnownIds.FruitTreePlot, "not-a-uuid")]
    public void UnreadableObservedIdentityIsContradictory(string plotUuid, string actionUuid)
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            plotUuid,
            actionUuid,
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Fact]
    public void SupportedActionReferenceOnUnexpectedPlotIsContradictory()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: true);

        Assert.Equal(AutoHarvestObservedPair.Contradictory, observed);
    }

    [Fact]
    public void UnrelatedActionOnSupportedPlotRemainsUnrelated()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            Guid.NewGuid().ToString("D"),
            exactFruitReferences: false,
            exactTreasureReferences: false,
            supportedActionReference: false);

        Assert.Equal(AutoHarvestObservedPair.Unrelated, observed);
    }

    [Fact]
    public void ExactCurrentReferencesRemainRecognized()
    {
        var observed = AutoHarvestIdentityPolicy.Classify(
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            exactFruitReferences: true,
            exactTreasureReferences: false,
            supportedActionReference: true);

        Assert.Equal(AutoHarvestObservedPair.FruitTree, observed);
    }

    [Fact]
    public void PolicyConstantsMatchGeneratedTypedIdentities()
    {
        Assert.Equal(AutoHarvestKnownIds.FruitTreePlot, KnownEntities.FruitTreePlot.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.FruitTreeCollect, KnownEntities.FruitTreeCollect.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreePlot, KnownEntities.TreasureTreePlot.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreeCollect, KnownEntities.TreasureTreeCollect.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.ActivePlotNodeActions, KnownEntities.ActivePlotNodeActions.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.CompletionScalingWeight, KnownEntities.CompletionScalingWeight.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.TreasureTreeRewardPool, KnownEntities.TreasureTreeRewardPool.Uuid.ToString("D"));
        Assert.Equal(AutoHarvestKnownIds.FruitTreeRewardPool, KnownEntities.FruitTreeRewardPool.Uuid.ToString("D"));
    }

    [Theory]
    [InlineData(AutoHarvestKnownIds.FruitTreePlot, AutoHarvestKnownIds.FruitTreeCollect)]
    [InlineData(AutoHarvestKnownIds.TreasureTreePlot, AutoHarvestKnownIds.TreasureTreeCollect)]
    public void SelectedReadyNativePhaseCyclePairIsSubmitted(string plotUuid, string actionUuid)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(plotUuid, actionUuid),
            currentLifecycleEpoch: 7);

        Assert.True(decision.ShouldSubmit);
        Assert.Equal(AutoHarvestRejectionReason.None, decision.RejectionReason);
    }

    [Fact]
    public void ExactKnownPlotAndActionMustBelongToTheSamePair()
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(AutoHarvestKnownIds.FruitTreePlot, AutoHarvestKnownIds.TreasureTreeCollect),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.UnsupportedPair);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected)]
    public void IdentityMustBePositivelyVerified(int identityValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(identity: (AutoHarvestEvidenceState)identityValue),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.IdentityUnverified);
    }

    [Fact]
    public void CandidateMustBeExplicitlySelected()
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(selected: false),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.NotSelected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(8)]
    public void StaleOrInvalidLifecycleCannotSubmit(long capturedEpoch)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(lifecycleEpoch: capturedEpoch),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.LifecycleChanged);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.PlotVisibilityUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.PlotNotVisible)]
    public void PlotVisibilityFailsClosed(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(plotVisibility: (AutoHarvestEvidenceState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.ActionAvailabilityUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.ActionUnavailable)]
    public void ActionAvailabilityFailsClosed(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(actionAvailability: (AutoHarvestEvidenceState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.PrerequisitesUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.PrerequisitesUnmet)]
    public void PrerequisitesFailClosed(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(prerequisites: (AutoHarvestEvidenceState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.ReadinessUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.NotReady)]
    public void NativeReadinessFailsClosed(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(readiness: (AutoHarvestEvidenceState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestActionSafetyState.Unknown, (int)AutoHarvestRejectionReason.PreservationUnknown)]
    [InlineData((int)AutoHarvestActionSafetyState.Destructive, (int)AutoHarvestRejectionReason.DestructiveAction)]
    [InlineData((int)AutoHarvestActionSafetyState.ResourceDrain, (int)AutoHarvestRejectionReason.ResourceDrainPresent)]
    [InlineData((int)AutoHarvestActionSafetyState.UnsafeCompletionEffects, (int)AutoHarvestRejectionReason.UnsafeCompletionEffects)]
    public void OnlyAuditedNativePhaseCycleActionsAreSafe(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(actionSafety: (AutoHarvestActionSafetyState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Theory]
    [InlineData((int)AutoHarvestEvidenceState.Unknown, (int)AutoHarvestRejectionReason.DuplicateStateUnknown)]
    [InlineData((int)AutoHarvestEvidenceState.Rejected, (int)AutoHarvestRejectionReason.AlreadyQueuedOrRunning)]
    public void DuplicateStateFailsClosed(int stateValue, int expectedValue)
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(noDuplicate: (AutoHarvestEvidenceState)stateValue),
            7);

        AssertRejected(decision, (AutoHarvestRejectionReason)expectedValue);
    }

    [Fact]
    public void UnknownActionSlotStateFailsClosed()
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(actionSlotAvailability: AutoHarvestEvidenceState.Unknown),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.ActionSlotStateUnknown);
    }

    [Fact]
    public void FullNativeActionSlotListBlocksSubmission()
    {
        var decision = AutoHarvestPolicy.Evaluate(
            ReadyCandidate(actionSlotAvailability: AutoHarvestEvidenceState.Rejected),
            7);

        AssertRejected(decision, AutoHarvestRejectionReason.NoActionSlot);
    }

    private static AutoHarvestCandidateSnapshot ReadyCandidate(
        string plotUuid = AutoHarvestKnownIds.FruitTreePlot,
        string actionUuid = AutoHarvestKnownIds.FruitTreeCollect,
        long lifecycleEpoch = 7,
        bool selected = true,
        AutoHarvestEvidenceState identity = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState plotVisibility = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState actionAvailability = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState prerequisites = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified,
        AutoHarvestActionSafetyState actionSafety = AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
        AutoHarvestEvidenceState noDuplicate = AutoHarvestEvidenceState.Verified,
        AutoHarvestEvidenceState actionSlotAvailability = AutoHarvestEvidenceState.Verified) =>
        new(
            plotUuid,
            actionUuid,
            lifecycleEpoch,
            selected,
            identity,
            plotVisibility,
            actionAvailability,
            prerequisites,
            readiness,
            actionSafety,
            noDuplicate,
            actionSlotAvailability);

    private static void AssertRejected(
        AutoHarvestDecision decision,
        AutoHarvestRejectionReason expectedReason)
    {
        Assert.False(decision.ShouldSubmit);
        Assert.Equal(expectedReason, decision.RejectionReason);
    }
}
