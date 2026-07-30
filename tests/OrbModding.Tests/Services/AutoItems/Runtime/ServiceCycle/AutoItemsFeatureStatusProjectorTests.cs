using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsFeatureStatusProjectorTests
{
    [Fact]
    public void OperationalProjectionCarriesNoReasonSummary()
    {
        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
        Assert.Empty(status.Summary);
    }

    [Fact]
    public void OperationalLifecycleObservationPublishesAnEmptyReason()
    {
        var generation = new ConfigGeneration(1);
        using var reporter = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoItemsFeatureId),
                "Auto Items",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration: 1),
            generation);

        Assert.True(reporter.ObserveRuntimeLifecycle(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            "Operational detail is not a blocking reason.",
            lifecycleGeneration: 2,
            generation));

        Assert.Equal(FeatureStatusState.Operational, reporter.Current.State);
        Assert.True(reporter.Current.Reason.IsEmpty);
        Assert.Equal(2, reporter.Current.LifecycleGeneration);
    }
}
