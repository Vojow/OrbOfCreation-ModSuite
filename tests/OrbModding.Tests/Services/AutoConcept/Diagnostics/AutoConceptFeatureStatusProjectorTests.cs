using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Diagnostics;

public sealed class AutoConceptFeatureStatusProjectorTests
{
    [Fact]
    public void ConfigurationHasPrecedenceOverEveryRuntimeCondition()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            pluginEnabled: false,
            featureEnabled: false,
            emergencyDisabled: true,
            owned: false,
            cycleObserved: false);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.State);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled, status.Reason);
    }

    [Fact]
    public void OwnershipLossIsReportedBeforeFirstCycleReadiness()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            pluginEnabled: true,
            featureEnabled: true,
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
            pluginEnabled: true,
            featureEnabled: true,
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
    }
}
