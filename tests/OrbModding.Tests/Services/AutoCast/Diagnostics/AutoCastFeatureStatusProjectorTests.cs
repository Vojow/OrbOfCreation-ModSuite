using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Diagnostics;

/// <summary>
/// What Auto Cast's health line says, and — more to the point — which of several simultaneously true
/// facts it chooses to say. The order of the terms is the whole subject here.
/// </summary>
public sealed class AutoCastFeatureStatusProjectorTests
{
    [Fact]
    public void AFeatureTheOperatorSwitchedOffSaysSoAndNothingElse()
    {
        // Every other term is true at once here. Reporting any of them would answer a question the
        // player did not ask and hide the one they did.
        var status = Project(
            pluginEnabled: false,
            featureEnabled: false,
            emergencyDisabled: true,
            owned: false,
            manualPaused: true,
            cycleObserved: false);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, status.Reason);
        Assert.Equal(AutoCastFeatureStatusProjector.ConfigurationDisabledSummary, status.Summary);
    }

    [Fact]
    public void ASuiteSwitchedOffBlocksBeforeAnythingItWouldHaveDone()
    {
        var status = Project(pluginEnabled: false, emergencyDisabled: true, owned: false);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, status.Reason);
    }

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
        bool pluginEnabled = true,
        bool featureEnabled = true,
        bool emergencyDisabled = false,
        bool owned = true,
        bool manualPaused = false,
        bool cycleObserved = true) =>
        AutoCastFeatureStatusProjector.Project(
            pluginEnabled,
            featureEnabled,
            emergencyDisabled,
            owned,
            manualPaused,
            cycleObserved);
}
