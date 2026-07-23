using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Diagnostics;

public sealed class AutoHarvestRuntimeDiagnosticsPublisherTests
{
    [Fact]
    public void PublisherRequiresAnExplicitDiagnosticsRegistry()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            new AutoHarvestRuntimeDiagnosticsPublisher(
                lifecycleGeneration: 1,
                AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
                AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
                AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
                null!));
    }

    [Fact]
    public void PublisherMapsPartialUnlockEvidenceWithoutTreatingLockedSiblingAsFailure()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            lifecycleGeneration: 7,
            new AutoHarvestPairHealth(
                AutoHarvestPair.FruitTree,
                selected: true,
                AutoHarvestPairHealthKind.NativeBusy),
            new AutoHarvestPairHealth(
                AutoHarvestPair.TreasureTree,
                selected: true,
                AutoHarvestPairHealthKind.ProgressionLocked),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(PluginIds.AutomataGuid, snapshot.Key.PluginId);
        Assert.Equal(AutomataFeatureStatuses.AutoHarvestFeatureId, snapshot.Key.FeatureId);
        Assert.Equal(AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, snapshot.Implementation);
        Assert.Equal(7, snapshot.LifecycleGeneration);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, snapshot.Capabilities[0].State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, snapshot.Capabilities[0].Reason.Code);
        Assert.Equal(FeatureStatusState.Locked, snapshot.Capabilities[1].State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, snapshot.Capabilities[1].Reason.Code);
    }

    [Fact]
    public void PublisherSuppressesEquivalentStateAndPublishesLifecycleReplacement()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var transitions = 0;
        registry.Transitioned += _ => transitions++;
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            7, fruit, treasure, AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, registry);

        publisher.PublishState(7, fruit, treasure);
        Assert.Equal(1, transitions);

        publisher.PublishState(8, fruit, treasure);
        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(8, snapshot.LifecycleGeneration);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void DisposalRemovesPublisherOwnership()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            1,
            AutoHarvestPairHealth.NotSelected(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        publisher.Dispose();

        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void FeatureScopedFailureProjectsAcrossEverySelectedCapability()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            3,
            new AutoHarvestPairHealth(
                AutoHarvestPair.FruitTree,
                selected: true,
                AutoHarvestPairHealthKind.ContractUnavailable,
                featureScoped: true),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        var capabilities = Assert.Single(registry.GetSnapshot()).Capabilities;
        Assert.All(capabilities, capability =>
            Assert.Equal(FeatureStatusState.ContractUnavailable, capability.State));
        Assert.All(capabilities, capability =>
            Assert.Contains("service", capability.Reason.Summary, System.StringComparison.OrdinalIgnoreCase));
    }

}
