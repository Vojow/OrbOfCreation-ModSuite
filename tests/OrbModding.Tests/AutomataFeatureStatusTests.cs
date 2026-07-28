using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataFeatureStatusTests
{
    [Fact]
    public void AutomataRegistersConfigurationSeparatelyFromLifecycleReadiness()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var added = 0;
        var changed = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Added) added++;
            if (transition.Kind == FeatureStatusTransitionKind.Changed) changed++;
        };
        using var statuses = new AutomataFeatureStatuses(config.Current, 7, registry);

        Assert.Equal(FeatureStatusState.NotReady, statuses.AutoBuy.Current.State);
        Assert.True(statuses.AutoBuy.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.False(statuses.AutoCast.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusState.NotReady, statuses.SpellLevel.Current.State);
        Assert.True(statuses.SpellLevel.Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
        Assert.All(registry.GetSnapshot(), status => Assert.Equal(7, status.LifecycleGeneration));
        Assert.Equal(5, added);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void AutomataReporterPublishesOnlyConditionTransitions()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        var changes = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed &&
                transition.Current?.Key.Equals(statuses.AutoBuy.Key) == true)
                changes++;
        };

        Assert.True(statuses.AutoBuy.ObserveOperational());
        for (var index = 0; index < 10_000; index++)
            Assert.False(statuses.AutoBuy.ObserveOperational());
        Assert.True(statuses.AutoBuy.Observe(
            true,
            FeatureStatusState.Degraded,
            FeatureStatusReasonCode.PartialCapabilityUnavailable,
            "Upgrade automation is unavailable."));
        for (var index = 0; index < 10_000; index++)
        {
            Assert.False(statuses.AutoBuy.Observe(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent wording does not create another transition."));
            Assert.False(statuses.AutoBuy.ObserveLifecycle(
                true,
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "Equivalent lifecycle observation does not create another transition.",
                1));
        }

        Assert.Equal(2, changes);
    }

    [Fact]
    public void AssemblyContractFailurePreservesConfigurationDisabledPrecedence()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);

        statuses.ObserveContractUnavailable(config.Current, 1, "Assembly contract mismatch.");

        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.AutoBuy.Current.State);
        Assert.Equal(FeatureStatusState.ContractUnavailable, statuses.SpellLevel.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
    }

    [Fact]
    public void GloballyDisabledAutomataAdvancesEveryFeatureOnLifecycleTransitionsThenStaysIdle()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.Enabled.Value = false;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 4, registry);
        var changes = 0;
        registry.Transitioned += transition =>
        {
            if (transition.Kind == FeatureStatusTransitionKind.Changed) changes++;
        };

        statuses.ObserveLifecycleNotReady(config.Current, 9);

        Assert.All(registry.GetSnapshot(), status => Assert.Equal(9, status.LifecycleGeneration));
        Assert.Equal(5, changes);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(config.Current, 9);

        Assert.Equal(5, changes);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.AutoBuy.Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoCast.Current.State);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoConcept.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ParentFeatureDisabled, statuses.SpellLevel.Current.Reason.Code);
        Assert.Equal(FeatureStatusState.ConfigurationDisabled, statuses.AutoHarvest.Current.State);
    }

    [Fact]
    public void StableLifecycleProjectionReusesCachedConfigurationDisabledSummaries()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, registry);
        var autoCastSummary = statuses.AutoCast.ConfigurationDisabledSummary;
        var autoConceptSummary = statuses.AutoConcept.ConfigurationDisabledSummary;

        statuses.ObserveLifecycleNotReady(config.Current, 2);
        Assert.Same(autoCastSummary, statuses.AutoCast.Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.AutoConcept.Current.Reason.Summary);

        for (var index = 0; index < 10_000; index++)
            statuses.ObserveLifecycleNotReady(config.Current, 2);

        Assert.Same(autoCastSummary, statuses.AutoCast.Current.Reason.Summary);
        Assert.Same(autoConceptSummary, statuses.AutoConcept.Current.Reason.Summary);
    }

    [Fact]
    public void AggregateRegistrationRollsBackEarlierKeysWhenALaterKeyIsAlreadyOwned()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var registry = new FeatureStatusRegistry();
        var duplicateKey = new FeatureStatusKey(
            PluginIds.SuiteGuid,
            AutomataFeatureStatuses.AutoConceptFeatureId);
        using var existing = registry.Register(new FeatureStatusSnapshot(
            duplicateKey,
            "Existing Auto Concept",
            false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Existing owner remains registered."),
            3));

        Assert.Throws<InvalidOperationException>(() =>
            new AutomataFeatureStatuses(config.Current, 3, registry));

        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(duplicateKey, snapshot.Key);
        Assert.Equal("Existing Auto Concept", snapshot.DisplayName);
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
            out _));
        Assert.False(registry.TryGet(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoCastFeatureId),
            out _));
    }

    [Fact]
    public void ControlsKeepConfiguredIntentOnAcrossRuntimeHealthTransitions()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        var registry = new FeatureStatusRegistry();
        using var statuses = new AutomataFeatureStatuses(config.Current, 3, registry);
        var control = new AutoCastToggleControl(config, () => statuses.AutoCast.Current);

        Assert.Equal(AutoCastToggleVisualState.On, control.State);
        Assert.True(control.Status.ConfiguredEnabled);

        statuses.AutoCast.Observe(
            true,
            FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable,
            "The native cast contract is unavailable.");

        Assert.Equal(AutoCastToggleVisualState.On, control.State);
        Assert.Equal(FeatureStatusState.ContractUnavailable, control.Status.State);
        Assert.Contains("Configured: Enabled", FeatureStatusPresenter.Format(control.Status));
        Assert.Contains("Runtime: Unavailable", FeatureStatusPresenter.Format(control.Status));

        statuses.AutoCast.ObserveOperational();
        Assert.Equal(AutoCastToggleVisualState.On, control.State);
    }
}
