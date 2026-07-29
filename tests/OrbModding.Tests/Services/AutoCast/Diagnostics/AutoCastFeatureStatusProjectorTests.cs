using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Diagnostics;

/// <summary>
/// What Auto Cast's running service says about runtime health. Saved intent is joined centrally.
/// </summary>
public sealed class AutoCastFeatureStatusProjectorTests
{
    [Fact]
    public void TheEmergencyStopOutranksWhoOwnsTheActionFamily()
    {
        // A suite that has stood down entirely is a bigger fact than which plugin holds a lease.
        var status = Project(emergencyDisabled: true, owned: false, manualPaused: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason);
        Assert.Equal(AutoCastFeatureStatusProjector.EmergencyDisabledSummary, status.Summary);
    }

    [Fact]
    public void AnotherPluginOwningSpellCastingIsReportedAsSuch()
    {
        var status = Project(owned: false, manualPaused: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Reason);
    }

    [Fact]
    public void TheManualPauseIsTheLastBlockingTermBecauseItIsTheOneThatResolvesItself()
    {
        var status = Project(manualPaused: true, cycleObserved: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ManualPause, status.Reason);
        Assert.Equal(AutoCastFeatureStatusProjector.ManualPauseSummary, status.Summary);
    }

    [Fact]
    public void AServiceThatHasNotRunYetSaysSoRatherThanClaimingToBeWorking()
    {
        var status = Project(cycleObserved: false);

        Assert.Equal(FeatureStatusState.NotReady, status.State);
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, status.Reason);
    }

    [Fact]
    public void EverythingClearAndACycleObservedReadsOperational()
    {
        var status = Project(cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
    }

    private static AutoCastFeatureStatus Project(
        bool emergencyDisabled = false,
        bool owned = true,
        bool manualPaused = false,
        bool cycleObserved = true) =>
        AutoCastFeatureStatusProjector.Project(
            emergencyDisabled,
            owned,
            manualPaused,
            cycleObserved);
}
