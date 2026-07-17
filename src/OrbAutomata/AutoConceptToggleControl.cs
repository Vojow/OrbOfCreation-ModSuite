namespace OrbAutomata;

internal sealed class AutoConceptToggleControl
{
    private readonly AutomataConfig _config;
    public AutoConceptToggleControl(AutomataConfig config) { _config = config; }

    internal AutomataConfig Config => _config;

    public AutoCastToggleVisualState State => _config.AutoConceptMode.Value switch
    {
        AutoConceptOperationMode.Disabled => AutoCastToggleVisualState.Off,
        AutoConceptOperationMode.Active when !_config.CanStartAutoConceptActively => AutoCastToggleVisualState.Blocked,
        AutoConceptOperationMode.Active => AutoCastToggleVisualState.On,
        _ => AutoCastToggleVisualState.Off,
    };

    public void Toggle() =>
        _config.AutoConceptMode.Value = _config.AutoConceptMode.Value == AutoConceptOperationMode.Active
            ? AutoConceptOperationMode.Disabled
            : AutoConceptOperationMode.Active;
}
