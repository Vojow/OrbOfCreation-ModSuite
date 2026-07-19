using System;
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

    public AutomataFeatureStatuses(
        AutomataConfig config,
        long lifecycleGeneration,
        FeatureStatusRegistry? registry = null)
    {
        var target = registry ?? FeatureStatusRegistry.Shared;
        AutomataFeatureStatusReporter? autoBuy = null;
        AutomataFeatureStatusReporter? autoCast = null;
        AutomataFeatureStatusReporter? autoConcept = null;
        AutomataFeatureStatusReporter? spellLevel = null;
        try
        {
            autoBuy = CreateInitialReporter(
                target, AutoBuyFeatureId, "Auto Buy", config.Enabled.Value,
                config.AutoBuyMode.Value == AutoBuyOperationMode.Active, true, lifecycleGeneration);
            autoCast = CreateInitialReporter(
                target, AutoCastFeatureId, "Auto Cast", config.Enabled.Value,
                config.AutoCastMode.Value == AutoCastOperationMode.Active, true, lifecycleGeneration);
            autoConcept = CreateInitialReporter(
                target, AutoConceptFeatureId, "Auto Concept", config.Enabled.Value,
                config.AutoConceptMode.Value == AutoConceptOperationMode.Active, true, lifecycleGeneration);
            spellLevel = CreateInitialReporter(
                target, SpellLevelFeatureId, "Spell Leveling", config.Enabled.Value,
                config.AutoLevelSpells.Value,
                config.AutoBuyMode.Value == AutoBuyOperationMode.Active,
                lifecycleGeneration);
        }
        catch
        {
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
    }

    public AutomataFeatureStatusReporter AutoBuy { get; }
    public AutomataFeatureStatusReporter AutoCast { get; }
    public AutomataFeatureStatusReporter AutoConcept { get; }
    public AutomataFeatureStatusReporter SpellLevel { get; }

    public void ObserveContractUnavailable(long lifecycleGeneration, string summary)
    {
        AutoBuy.ObserveLifecycle(true, FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable, summary, lifecycleGeneration);
        AutoCast.ObserveLifecycle(true, FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable, summary, lifecycleGeneration);
        AutoConcept.ObserveLifecycle(true, FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable, summary, lifecycleGeneration);
        SpellLevel.ObserveLifecycle(true, FeatureStatusState.ContractUnavailable, FeatureStatusReasonCode.ContractUnavailable, summary, lifecycleGeneration);
    }

    public void ObserveLifecycleNotReady(AutomataConfig config, long lifecycleGeneration)
    {
        ObserveLifecycleFeature(
            AutoBuy,
            config.Enabled.Value,
            config.AutoBuyMode.Value == AutoBuyOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoCast,
            config.Enabled.Value,
            config.AutoCastMode.Value == AutoCastOperationMode.Active,
            lifecycleGeneration);
        ObserveLifecycleFeature(
            AutoConcept,
            config.Enabled.Value,
            config.AutoConceptMode.Value == AutoConceptOperationMode.Active,
            lifecycleGeneration);

        if (!config.AutoLevelSpells.Value)
        {
            ObserveConfigurationDisabled(SpellLevel, lifecycleGeneration);
        }
        else if (!config.Enabled.Value || config.AutoBuyMode.Value == AutoBuyOperationMode.Disabled)
        {
            SpellLevel.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ParentFeatureDisabled,
                !config.Enabled.Value
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
        var key = new FeatureStatusKey(PluginIds.AutomataGuid, featureId);
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
        SpellLevel.Dispose();
        AutoConcept.Dispose();
        AutoCast.Dispose();
        AutoBuy.Dispose();
    }
}

internal static class AutomataFeatureStatusVisuals
{
    public static AutoCastToggleVisualState ToVisualState(in FeatureStatusSnapshot status) => status.State switch
    {
        FeatureStatusState.ConfigurationDisabled => AutoCastToggleVisualState.Off,
        FeatureStatusState.Operational => AutoCastToggleVisualState.On,
        FeatureStatusState.Locked or FeatureStatusState.NotReady => AutoCastToggleVisualState.Waiting,
        FeatureStatusState.TemporarilyBlocked when status.Reason.Code is
            FeatureStatusReasonCode.QueueFull or
            FeatureStatusReasonCode.NativeBusy or
            FeatureStatusReasonCode.ManualPause or
            FeatureStatusReasonCode.TargetingInProgress or
            FeatureStatusReasonCode.CapacityExceeded => AutoCastToggleVisualState.Waiting,
        _ => AutoCastToggleVisualState.Blocked,
    };
}
