using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutomataFeatureStatusReporter : IDisposable
{
    private readonly FeatureStatusRegistration _registration;

    public AutomataFeatureStatusReporter(
        FeatureStatusRegistry registry,
        FeatureStatusSnapshot initialStatus)
    {
        Key = initialStatus.Key;
        DisplayName = initialStatus.DisplayName;
        ConfigurationDisabledSummary = initialStatus.State == FeatureStatusState.ConfigurationDisabled
            ? initialStatus.Reason.Summary
            : DisplayName + " is disabled by configuration.";
        Current = initialStatus;
        _registration = registry.Register(Current);
    }

    public FeatureStatusKey Key { get; }
    public string DisplayName { get; }
    public string ConfigurationDisabledSummary { get; }
    public FeatureStatusSnapshot Current { get; private set; }

    public bool ObserveOperational() => Observe(
        true,
        FeatureStatusState.Operational,
        FeatureStatusReasonCode.None,
        string.Empty);

    public bool Observe(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary)
    {
        if (Current.ConfiguredEnabled == configuredEnabled &&
            Current.State == state &&
            Current.Reason.Code == reasonCode)
            return false;
        var status = new FeatureStatusSnapshot(
            Key,
            DisplayName,
            configuredEnabled,
            state,
            reasonCode == FeatureStatusReasonCode.None
                ? default
                : new FeatureStatusReason(reasonCode, summary),
            Current.LifecycleGeneration);
        if (!_registration.Update(status)) return false;
        Current = status;
        return true;
    }

    public bool ObserveLifecycle(
        bool configuredEnabled,
        FeatureStatusState state,
        FeatureStatusReasonCode reasonCode,
        string summary,
        long lifecycleGeneration)
    {
        if (Current.ConfiguredEnabled == configuredEnabled &&
            Current.State == state &&
            Current.Reason.Code == reasonCode &&
            Current.LifecycleGeneration == lifecycleGeneration)
            return false;
        var status = new FeatureStatusSnapshot(
            Key,
            DisplayName,
            configuredEnabled,
            state,
            new FeatureStatusReason(reasonCode, summary),
            lifecycleGeneration);
        if (!_registration.Update(status)) return false;
        Current = status;
        return true;
    }

    public void Dispose() => _registration.Dispose();
}

internal sealed class AutomataFeatureStatuses : IDisposable
{
    internal const string AutoBuyFeatureId = "AutoBuy";
    internal const string AutoCastFeatureId = "AutoCast";
    internal const string AutoConceptFeatureId = "AutoConcept";
    internal const string SpellLevelFeatureId = "SpellLevel";
    internal const string AutoHarvestFeatureId = "AutoHarvest";

    public AutomataFeatureStatuses(
        SuiteRuntimeConfiguration config,
        long lifecycleGeneration,
        FeatureStatusRegistry? registry = null)
    {
        var target = registry ?? FeatureStatusRegistry.Shared;
        AutomataFeatureStatusReporter? autoBuy = null;
        AutomataFeatureStatusReporter? autoCast = null;
        AutomataFeatureStatusReporter? autoConcept = null;
        AutomataFeatureStatusReporter? spellLevel = null;
        AutomataFeatureStatusReporter? autoHarvest = null;
        try
        {
            autoBuy = CreateInitialReporter(
                target, AutoBuyFeatureId, "Auto Buy", config.General.Enabled,
                config.AutoBuy.Mode == AutoBuyOperationMode.Active, true, lifecycleGeneration);
            autoCast = CreateInitialReporter(
                target, AutoCastFeatureId, "Auto Cast", config.General.Enabled,
                config.AutoCast.Mode == AutoCastOperationMode.Active, true, lifecycleGeneration);
            autoConcept = CreateInitialReporter(
                target, AutoConceptFeatureId, "Auto Concept", config.General.Enabled,
                config.AutoConcept.Mode == AutoConceptOperationMode.Active, true, lifecycleGeneration);
            spellLevel = CreateInitialReporter(
                target, SpellLevelFeatureId, "Spell Leveling", config.General.Enabled,
                config.AutoBuy.AutoLevelSpells,
                config.AutoBuy.Mode == AutoBuyOperationMode.Active,
                lifecycleGeneration);
            autoHarvest = CreateInitialReporter(
                target, AutoHarvestFeatureId, "Auto Harvest", config.General.Enabled,
                IsAutoHarvestConfigured(config), true, lifecycleGeneration);
        }
        catch
        {
            autoHarvest?.Dispose();
            spellLevel?.Dispose();
            autoConcept?.Dispose();
            autoCast?.Dispose();
            autoBuy?.Dispose();
            throw;
        }
        AutoBuy = autoBuy!;
        AutoCast = autoCast!;
        AutoConcept = autoConcept!;
        SpellLevel = spellLevel!;
        AutoHarvest = autoHarvest!;
    }

    public AutomataFeatureStatusReporter AutoBuy { get; }
    public AutomataFeatureStatusReporter AutoCast { get; }
    public AutomataFeatureStatusReporter AutoConcept { get; }
    public AutomataFeatureStatusReporter SpellLevel { get; }
    public AutomataFeatureStatusReporter AutoHarvest { get; }

    public void ObserveContractUnavailable(SuiteRuntimeConfiguration config, long lifecycleGeneration, string summary)
    {
        ObserveContractFeature(AutoBuy, config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoCast, config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(AutoConcept, config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active, true, lifecycleGeneration, summary);
        ObserveContractFeature(SpellLevel, config.General.Enabled,
            config.AutoBuy.AutoLevelSpells,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            lifecycleGeneration, summary);
        ObserveContractFeature(AutoHarvest, config.General.Enabled,
            IsAutoHarvestConfigured(config), true, lifecycleGeneration, summary);
    }

    public void ObserveLifecycleNotReady(SuiteRuntimeConfiguration config, long lifecycleGeneration)
    {
        ObserveLifecycleFeature(
            AutoBuy,
            config.General.Enabled,
            config.AutoBuy.Mode == AutoBuyOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoCast,
            config.General.Enabled,
            config.AutoCast.Mode == AutoCastOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoConcept,
            config.General.Enabled,
            config.AutoConcept.Mode == AutoConceptOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoHarvest,
            config.General.Enabled,
            IsAutoHarvestConfigured(config),
            lifecycleGeneration);

        if (!config.AutoBuy.AutoLevelSpells)
        {
            ObserveConfigurationDisabled(SpellLevel, lifecycleGeneration);
        }
        else if (!config.General.Enabled || config.AutoBuy.Mode == AutoBuyOperationMode.Disabled)
        {
            SpellLevel.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !config.General.Enabled
                    ? "Automata is disabled by configuration."
                    : "Auto Buy is disabled by configuration.",
                lifecycleGeneration);
        }
        else
        {
            ObserveGameplayNotReady(SpellLevel, lifecycleGeneration);
        }
    }

    private static void ObserveLifecycleFeature(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        long lifecycleGeneration)
    {
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration);
            return;
        }
        if (!pluginEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Automata is disabled by configuration.",
                lifecycleGeneration);
            return;
        }
        ObserveGameplayNotReady(reporter, lifecycleGeneration);
    }

    private static void ObserveContractFeature(
        AutomataFeatureStatusReporter reporter,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        long lifecycleGeneration,
        string summary)
    {
        if (!featureEnabled)
        {
            ObserveConfigurationDisabled(reporter, lifecycleGeneration);
            return;
        }
        if (!pluginEnabled || !parentEnabled)
        {
            reporter.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !pluginEnabled
                    ? "Automata is disabled by configuration."
                    : "The parent automation feature is disabled by configuration.",
                lifecycleGeneration);
            return;
        }
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.ContractUnavailable,
            FeatureStatusReasonCode.ContractUnavailable,
            summary,
            lifecycleGeneration);
    }

    private static bool IsAutoHarvestConfigured(SuiteRuntimeConfiguration config) =>
        config.AutoHarvest.Mode == AutoHarvestOperationMode.Active &&
        (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees);

    private static void ObserveConfigurationDisabled(
        AutomataFeatureStatusReporter reporter,
        long lifecycleGeneration) =>
        reporter.ObserveLifecycle(
            false,
            FeatureStatusState.ConfigurationDisabled,
            FeatureStatusReasonCode.ConfigurationDisabled,
            reporter.ConfigurationDisabledSummary,
            lifecycleGeneration);

    private static void ObserveGameplayNotReady(
        AutomataFeatureStatusReporter reporter,
        long lifecycleGeneration) =>
        reporter.ObserveLifecycle(
            true,
            FeatureStatusState.NotReady,
            FeatureStatusReasonCode.GameplayNotReady,
            "Gameplay lifecycle is not ready.",
            lifecycleGeneration);

    private static AutomataFeatureStatusReporter CreateInitialReporter(
        FeatureStatusRegistry registry,
        string featureId,
        string displayName,
        bool pluginEnabled,
        bool featureEnabled,
        bool parentEnabled,
        long lifecycleGeneration)
    {
        var key = new FeatureStatusKey(PluginIds.SuiteGuid, featureId);
        FeatureStatusSnapshot status;
        if (!featureEnabled)
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                false,
                FeatureStatusState.ConfigurationDisabled,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ConfigurationDisabled,
                    $"{displayName} is disabled by configuration."),
                lifecycleGeneration);
        }
        else if (!pluginEnabled || !parentEnabled)
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                true,
                FeatureStatusState.TemporarilyBlocked,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.ParentFeatureDisabled,
                    !pluginEnabled
                        ? "Automata is disabled by configuration."
                        : "Auto Buy is disabled by configuration."),
                lifecycleGeneration);
        }
        else
        {
            status = new FeatureStatusSnapshot(
                key,
                displayName,
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration);
        }
        return new AutomataFeatureStatusReporter(registry, status);
    }

    public void Dispose()
    {
        AutoHarvest.Dispose();
        SpellLevel.Dispose();
        AutoConcept.Dispose();
        AutoCast.Dispose();
        AutoBuy.Dispose();
    }
}

internal static class AutomataFeatureStatusVisuals
{
    public static AutoCastToggleVisualState ToVisualState(in FeatureStatusSnapshot status) =>
        FeatureStatusPresenter.Present(status).ConfiguredState == FeatureConfiguredPresentationState.On
            ? AutoCastToggleVisualState.On
            : AutoCastToggleVisualState.Off;
}
