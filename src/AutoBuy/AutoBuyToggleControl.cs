using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly IAutomataConfigurationEditor _config;
    private readonly System.Func<AutoSpellLevelCapability> _readSpellLevelCapability;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;
    private readonly System.Func<FeatureStatusSnapshot>? _readSpellLevelStatus;
    public AutoBuyToggleControl(
        IAutomataConfigurationEditor config,
        System.Func<AutoSpellLevelCapability>? readSpellLevelCapability = null,
        System.Func<FeatureStatusSnapshot>? readStatus = null,
        System.Func<FeatureStatusSnapshot>? readSpellLevelStatus = null)
    {
        _config = config;
        _readSpellLevelCapability = readSpellLevelCapability ?? (() => AutoSpellLevelCapability.Locked);
        _readStatus = readStatus;
        _readSpellLevelStatus = readSpellLevelStatus;
    }
    internal SuiteRuntimeConfiguration Config => _config.Current;
    internal AutoSpellLevelCapability SpellLevelCapability => _readSpellLevelCapability();
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();
    internal FeatureStatusSnapshot SpellLevelStatus => _readSpellLevelStatus?.Invoke() ?? CreateFallbackSpellLevelStatus();
    public AutoCastToggleVisualState State => AutomataFeatureStatusVisuals.ToVisualState(Status);
    public void Toggle() => _config.ToggleAutoBuy();

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = Config.AutoBuy.Mode == AutoBuyOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
            "Auto Buy",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !Config.CanStartAutoBuyActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Buy is disabled by configuration.")
                : !Config.CanStartAutoBuyActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }

    private FeatureStatusSnapshot CreateFallbackSpellLevelStatus()
    {
        var configured = Config.AutoBuy.AutoLevelSpells;
        var state = !configured
            ? FeatureStatusState.ConfigurationDisabled
            : !Config.CanStartAutoBuyActively
                ? FeatureStatusState.TemporarilyBlocked
                : SpellLevelCapability == AutoSpellLevelCapability.Locked
                    ? FeatureStatusState.Locked
                    : FeatureStatusState.Operational;
        var reason = state switch
        {
            FeatureStatusState.ConfigurationDisabled => new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Spell Leveling is disabled by configuration."),
            FeatureStatusState.TemporarilyBlocked => new FeatureStatusReason(
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Auto Buy is not active."),
            FeatureStatusState.Locked => new FeatureStatusReason(
                FeatureStatusReasonCode.ProgressionLocked,
                "Spell leveling has not been unlocked."),
            _ => default,
        };
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.SpellLevelFeatureId),
            "Spell Leveling",
            configured,
            state,
            reason);
    }
}
