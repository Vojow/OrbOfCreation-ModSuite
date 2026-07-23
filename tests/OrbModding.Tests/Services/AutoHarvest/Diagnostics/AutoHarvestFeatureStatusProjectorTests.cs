using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestFeatureStatusProjectorTests
{
    [Theory]
    [InlineData((int)AutoHarvestPairHealthKind.ProgressionLocked)]
    [InlineData((int)AutoHarvestPairHealthKind.NativeBusy)]
    [InlineData((int)AutoHarvestPairHealthKind.QueueBlocked)]
    public void EligiblePairKeepsFeatureOperationalWhenSiblingIsOrdinarilyUnavailable(int siblingKind)
    {
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = Selected(AutoHarvestPair.TreasureTree, (AutoHarvestPairHealthKind)siblingKind);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Operational, result.State);
        Assert.Equal(FeatureStatusReasonCode.None, result.Reason);
    }

    [Theory]
    [InlineData((int)AutoHarvestPairHealthKind.ContractUnavailable)]
    [InlineData((int)AutoHarvestPairHealthKind.Faulted)]
    public void EligiblePairReportsPartialCapabilityWhenSiblingHasFailed(int siblingKind)
    {
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = Selected(AutoHarvestPair.TreasureTree, (AutoHarvestPairHealthKind)siblingKind);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, result.Reason);
    }

    [Fact]
    public void UnlockedWaitingPairBeatsLockedSibling()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.NativeBusy);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ProgressionLocked);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, result.Reason);
    }

    [Fact]
    public void AllSelectedLockedReportsLocked()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ProgressionLocked);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ProgressionLocked);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Locked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, result.Reason);
    }

    [Fact]
    public void UnselectedSiblingDoesNotAffectSelectedLockedPair()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ProgressionLocked);
        var treasure = AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Locked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, result.Reason);
    }

    private static AutoHarvestPairHealth Selected(
        AutoHarvestPair pair,
        AutoHarvestPairHealthKind kind) =>
        new(pair, selected: true, kind);
}
