namespace OrbAutomata;

internal enum AutoCastToggleVisualState
{
    Off,
    On,
    Blocked,
}

internal sealed class AutoCastToggleControl
{
    private readonly AutomataConfig _config;

    public AutoCastToggleControl(AutomataConfig config)
    {
        _config = config;
    }

    internal AutomataConfig Config => _config;

    public AutoCastToggleVisualState State
    {
        get
        {
            return _config.AutoCastMode.Value switch
            {
                AutoCastOperationMode.Disabled => AutoCastToggleVisualState.Off,
                AutoCastOperationMode.Active when !_config.CanStartAutoCastActively => AutoCastToggleVisualState.Blocked,
                AutoCastOperationMode.Active => AutoCastToggleVisualState.On,
                _ => AutoCastToggleVisualState.Off,
            };
        }
    }

    public void Toggle()
    {
        _config.AutoCastMode.Value = _config.AutoCastMode.Value == AutoCastOperationMode.Active
            ? AutoCastOperationMode.Disabled
            : AutoCastOperationMode.Active;
    }
}
