namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly AutomataConfig _config;
    public AutoBuyToggleControl(AutomataConfig config) { _config = config; }
    internal AutomataConfig Config => _config;
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
