namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly AutomataConfig _config;
    private readonly System.Func<AutoSpellLevelCapability> _readSpellLevelCapability;
    public AutoBuyToggleControl(
        AutomataConfig config,
        System.Func<AutoSpellLevelCapability>? readSpellLevelCapability = null)
    {
        _config = config;
        _readSpellLevelCapability = readSpellLevelCapability ?? (() => AutoSpellLevelCapability.Locked);
    }
    internal AutomataConfig Config => _config;
    internal AutoSpellLevelCapability SpellLevelCapability => _readSpellLevelCapability();
    public AutoCastToggleVisualState State => _config.AutoBuyMode.Value switch
    {
        AutoBuyOperationMode.Disabled => AutoCastToggleVisualState.Off,
        AutoBuyOperationMode.Active when !_config.CanStartAutoBuyActively => AutoCastToggleVisualState.Blocked,
        AutoBuyOperationMode.Active => AutoCastToggleVisualState.On,
        _ => AutoCastToggleVisualState.Off,
    };
    public void Toggle() => _config.AutoBuyMode.Value = _config.AutoBuyMode.Value == AutoBuyOperationMode.Active
        ? AutoBuyOperationMode.Disabled : AutoBuyOperationMode.Active;
}
