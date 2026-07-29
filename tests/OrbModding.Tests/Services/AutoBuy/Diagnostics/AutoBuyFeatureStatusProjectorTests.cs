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

    private static AutoBuyFeatureStatus Project(
        bool emergencyDisabled = false,
        AutoBuyCandidateKinds owned = AutoBuyCandidateKinds.All,
        bool cycleObserved = true) =>
        AutoBuyFeatureStatusProjector.Project(
            emergencyDisabled,
            owned,
            cycleObserved);
}
