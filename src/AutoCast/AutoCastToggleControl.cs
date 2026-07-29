using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal enum AutoCastToggleVisualState
{
    Off,
    On,
}

internal sealed class AutoCastToggleControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly System.Func<FeatureStatusSnapshot> _readStatus;

    public AutoCastToggleControl(
        AutomataConfigurationStore configuration,
        System.Func<FeatureStatusSnapshot> readStatus)
    {
        _configuration = configuration;
        _readStatus = readStatus;
    }

    internal SuiteRuntimeConfiguration Config => _configuration.Current;
    internal FeatureStatusSnapshot Status => _readStatus();

    public AutoCastToggleVisualState State =>
        _configuration.Current.AutoCast.Mode == AutoCastOperationMode.Active
            ? AutoCastToggleVisualState.On
            : AutoCastToggleVisualState.Off;

    public void Toggle() => _configuration.ToggleAutoCast();
}
