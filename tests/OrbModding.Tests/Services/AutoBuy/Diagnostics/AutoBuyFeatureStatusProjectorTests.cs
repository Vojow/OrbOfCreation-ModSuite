using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyFeatureStatusProjectorTests
{
    [Fact]
    public void ConfiguredAndOwnedFeatureReportsOperationalOnceItHasEvaluated()
    {
        var result = Project(cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, result.State);
        Assert.Equal(FeatureStatusReasonCode.None, result.Reason);
        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public void ConfiguredFeatureWaitsUntilItHasEvaluated()
    {
        var result = Project(cycleObserved: false);

        Assert.Equal(FeatureStatusState.NotReady, result.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, result.Reason);
    }

    [Fact]
    public void EmergencyDisableBlocksAnEnabledFeature()
    {
        var result = Project(emergencyDisabled: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, result.Reason);
    }

    [Fact]
    public void LosingEveryOwnedPurchaseKindReportsAnActionFamilyConflict()
    {
        var result = Project(owned: AutoBuyCandidateKinds.None);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, result.Reason);
    }

    [Fact]
    public void LosingOneOfTwoSelectedPurchaseKindsReportsDegraded()
    {
        var result = Project(owned: AutoBuyCandidateKinds.Structures);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, result.Reason);
    }

    [Theory]
    [InlineData((int)AutoBuyDecisionBlockReason.OwningViewUnavailable, (int)FeatureStatusReasonCode.ProgressionLocked)]
    [InlineData((int)AutoBuyDecisionBlockReason.OwningViewRelationMissing, (int)FeatureStatusReasonCode.ContractUnavailable)]
    [InlineData((int)AutoBuyDecisionBlockReason.OwningViewRelationUnreadable, (int)FeatureStatusReasonCode.EvidenceUnavailable)]
    [InlineData((int)AutoBuyDecisionBlockReason.OwningViewRelationContradictory, (int)FeatureStatusReasonCode.ContractMismatch)]
    public void TotalRelationExclusionCannotReportOperational(int block, int reason)
    {
        var result = AutoBuyFeatureStatusProjector.Project(
            emergencyDisabled: false,
            AutoBuyCandidateKinds.All,
            cycleObserved: true,
            (AutoBuyDecisionBlockReason)block);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal((FeatureStatusReasonCode)reason, result.Reason);
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public void MixedTotalRelationExclusionReportsDegradedInsteadOfOperational()
    {
        var result = AutoBuyFeatureStatusProjector.Project(
            emergencyDisabled: false,
            AutoBuyCandidateKinds.All,
            cycleObserved: true,
            AutoBuyDecisionBlockReason.MixedPurchaseViewRelations);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.EvidenceUnavailable, result.Reason);
        Assert.Contains("more than one relation reason", result.Summary);
    }

    private static AutoBuyFeatureStatus Project(
        bool emergencyDisabled = false,
        AutoBuyCandidateKinds owned = AutoBuyCandidateKinds.All,
        bool cycleObserved = true) =>
        AutoBuyFeatureStatusProjector.Project(
            emergencyDisabled,
            owned,
            cycleObserved);
}
