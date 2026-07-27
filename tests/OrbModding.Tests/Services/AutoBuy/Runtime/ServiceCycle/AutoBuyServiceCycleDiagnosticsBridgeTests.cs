using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyServiceCycleDiagnosticsBridgeTests
{
    private const long Lifecycle = 7;

    /// <summary>
    /// The regression this bridge exists for: with gameplay live and nothing faulted, the operator
    /// turning Auto Buy on has to move the feature status, because that snapshot is what the toggle
    /// button, its tooltip, and the Mod Config health row all read.
    /// </summary>
    [Fact]
    public void ChangingTheModeWhileGameplayIsLiveMovesTheFeatureStatus()
    {
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            Configuration(active: false),
            AutoBuyCandidateKinds.All,
            status);

        Assert.False(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.Current.State);

        bridge.ObserveConfiguration(Configuration(active: true), AutoBuyCandidateKinds.All);

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.NotEqual(FeatureStatusState.ConfigurationDisabled, status.Current.State);

        bridge.ObserveConfiguration(Configuration(active: false), AutoBuyCandidateKinds.All);

        Assert.False(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.Current.State);
    }

    /// <summary>
    /// The refusal responder writes the mode off and publishes why. The configuration publication one
    /// frame later must carry that account forward rather than flatten it to "disabled by
    /// configuration".
    /// </summary>
    [Fact]
    public void ARefusalStandDownSurvivesTheConfigurationPublicationItCaused()
    {
        const string summary =
            "Auto Buy planned a purchase the game would not take. Auto Buy disabled itself.";
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            Configuration(active: true),
            AutoBuyCandidateKinds.All,
            status);

        status.Observe(
            false,
            FeatureStatusState.ConfigurationDisabled,
            FeatureStatusReasonCode.InvariantViolation,
            summary);
        bridge.ObserveConfiguration(Configuration(active: false), AutoBuyCandidateKinds.All);

        Assert.Equal(FeatureStatusState.ConfigurationDisabled, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, status.Current.Reason.Code);
        Assert.Equal(summary, status.Current.Reason.Summary);
    }

    [Fact]
    public void LosingPurchaseOwnershipBlocksAnEnabledFeature()
    {
        var registry = new FeatureStatusRegistry();
        using var status = Reporter(registry);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            Lifecycle,
            Configuration(active: true),
            AutoBuyCandidateKinds.All,
            status);

        bridge.ObserveConfiguration(Configuration(active: true), AutoBuyCandidateKinds.None);

        Assert.True(status.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Current.Reason.Code);
    }

    /// <summary>The cycle's own evidence, not the configuration, is what turns waiting into running.</summary>
    [Fact]
    public void AnEvaluatedCycleReportsOperational()
    {
        var configuration = Configuration(active: true);
        var definition = AutoBuyService.Define(new CommittingActions());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(configuration);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var featureRegistry = new FeatureStatusRegistry();
        using var status = Reporter(featureRegistry, lifecycle: 1);
        var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
            1,
            configuration,
            AutoBuyCandidateKinds.All,
            status);

        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);

        pump.PumpFrame(1);
        Assert.True(registration.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        var response = pump.PumpFrame(2);
        bridge.Observe(pump, in response, AutoBuyCandidateKinds.All);
        var quiet = pump.PumpFrame(3);
        bridge.Observe(pump, in quiet, AutoBuyCandidateKinds.All);

        Assert.Equal(FeatureStatusState.Operational, status.Current.State);

        bridge.ObserveLifecycle(2, configuration, AutoBuyCandidateKinds.All);

        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);
    }

    private static AutomataFeatureStatusReporter Reporter(
        FeatureStatusRegistry registry,
        long lifecycle = Lifecycle) =>
        new(
            registry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
                "Auto Buy",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycle));

    private static SuiteRuntimeConfiguration Configuration(bool active) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = active ? AutoBuyOperationMode.Active : AutoBuyOperationMode.Disabled,
                IncludeStructures = true,
                IncludeUpgrades = true,
                EvaluationIntervalSeconds = 0.01f,
            },
        };

    private sealed class CommittingActions : IAutoBuyCycleActionPort
    {
        public ServiceActionResult TryExecute(
            in AutoBuyCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
    }
}
