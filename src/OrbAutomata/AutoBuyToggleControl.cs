using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly AutomataConfig _config;
    private readonly System.Func<AutoSpellLevelCapability> _readSpellLevelCapability;
    private readonly System.Func<AutomationDecision?> _readLatestDecision;
    public AutoBuyToggleControl(
        AutomataConfig config,
        System.Func<AutoSpellLevelCapability>? readSpellLevelCapability = null,
        System.Func<AutomationDecision?>? readLatestDecision = null)
    {
        _config = config;
        _readSpellLevelCapability = readSpellLevelCapability ?? (() => AutoSpellLevelCapability.Locked);
        _readLatestDecision = readLatestDecision ?? (() => null);
    }
    internal AutomataConfig Config => _config;
    internal AutoSpellLevelCapability SpellLevelCapability => _readSpellLevelCapability();
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
    public AutoCastToggleVisualState State => _config.AutoBuyMode.Value switch
    {
        AutoBuyOperationMode.Disabled => AutoCastToggleVisualState.Off,
        AutoBuyOperationMode.Active when !_config.CanStartAutoBuyActively => AutoCastToggleVisualState.Blocked,
        AutoBuyOperationMode.Active => AutoCastToggleVisualState.On,
        _ => AutoCastToggleVisualState.Off,
    };
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
}
