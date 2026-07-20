using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly AutomataConfig _config;
    private readonly System.Func<AutoSpellLevelCapability> _readSpellLevelCapability;
    private readonly System.Func<AutomationDecision?> _readLatestDecision;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;
    private readonly System.Func<FeatureStatusSnapshot>? _readSpellLevelStatus;
    public AutoBuyToggleControl(
        AutomataConfig config,
        System.Func<AutoSpellLevelCapability>? readSpellLevelCapability = null,
        System.Func<AutomationDecision?>? readLatestDecision = null,
        System.Func<FeatureStatusSnapshot>? readStatus = null,
        System.Func<FeatureStatusSnapshot>? readSpellLevelStatus = null)
    {
        _config = config;
        _readSpellLevelCapability = readSpellLevelCapability ?? (() => AutoSpellLevelCapability.Locked);
        _readLatestDecision = readLatestDecision ?? (() => null);
        _readStatus = readStatus;
        _readSpellLevelStatus = readSpellLevelStatus;
    }
    internal AutomataConfig Config => _config;
    internal AutoSpellLevelCapability SpellLevelCapability => _readSpellLevelCapability();
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();
    internal FeatureStatusSnapshot SpellLevelStatus => _readSpellLevelStatus?.Invoke() ?? CreateFallbackSpellLevelStatus();
    internal AutomationDecision? LatestDecision
    {
        get
        {
            if (_config.AutoBuyMode.Value == AutoBuyOperationMode.Disabled)
            {
                return CreateConfigurationDecision("Auto Buy mode is disabled.");
            }

            if (!_config.CanStartAutoBuyActively)
            {
                return CreateConfigurationDecision("Automata Emergency Disable is active.");
            }

            return _readLatestDecision();
        }
    }
    public AutoCastToggleVisualState State => AutomataFeatureStatusVisuals.ToVisualState(Status);
    public void Toggle() => _config.AutoBuyMode.Value = _config.AutoBuyMode.Value == AutoBuyOperationMode.Active
        ? AutoBuyOperationMode.Disabled : AutoBuyOperationMode.Active;

    private static AutomationDecision CreateConfigurationDecision(string detail) =>
        new AutomationDecision(
            AutoBuyDecision.FeatureId,
            "Operate",
            AutomationDecisionDisposition.Skipped,
            AutomationDecisionCode.ConfigurationDisabled,
            retryTriggers: AutomationRetryTrigger.Configuration,
            technicalDetail: detail);

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = _config.AutoBuyMode.Value == AutoBuyOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.AutoBuyFeatureId),
            "Auto Buy",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !_config.CanStartAutoBuyActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Buy is disabled by configuration.")
                : !_config.CanStartAutoBuyActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }

    private FeatureStatusSnapshot CreateFallbackSpellLevelStatus()
    {
        var configured = _config.AutoLevelSpells.Value;
        var state = !configured
            ? FeatureStatusState.ConfigurationDisabled
            : !_config.CanStartAutoBuyActively
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
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.SpellLevelFeatureId),
            "Spell Leveling",
            configured,
            state,
            reason);
    }
}
