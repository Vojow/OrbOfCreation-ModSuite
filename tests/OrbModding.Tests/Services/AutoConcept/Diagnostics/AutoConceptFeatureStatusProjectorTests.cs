using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Diagnostics;

public sealed class AutoConceptFeatureStatusProjectorTests
{
    [Fact]
    public void OwnershipLossIsReportedBeforeFirstCycleReadiness()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: false,
            cycleObserved: false);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Reason);
    }

    [Fact]
    public void AnObservedOwnedCycleIsOperational()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
    }

    [Fact]
    public void TrainingWaitIsHighlightedWithoutTreatingItAsAFailure()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idleReason: AutoConceptIdleReason.WaitingForTraining);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, status.Reason);
        Assert.Contains("training period", status.Summary);
    }

    [Fact]
    public void MissingUnlockedAssignableReplacementIsProgressionLocked()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idleReason: AutoConceptIdleReason.NoUnlockedAssignableReplacement);

        Assert.Equal(FeatureStatusState.Locked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, status.Reason);
        Assert.Equal(
            "No other unlocked, allowed concept can be assigned.",
            status.Summary);
    }

    [Fact]
    public void TemporarilyRefusedUnlockedReplacementsShowTheNativeSafetyRetry()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idleReason: AutoConceptIdleReason.WaitingForCandidateRetry);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, status.Reason);
        Assert.Contains("slot or resource safety", status.Summary);
        Assert.Contains("retry", status.Summary);
    }
}
