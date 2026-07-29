using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoHarvestToggleControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly System.Func<FeatureStatusSnapshot> _readStatus;

    internal AutoHarvestToggleControl(
        AutomataConfigurationStore configuration,
        System.Func<FeatureStatusSnapshot> readStatus)
    {
        _configuration = configuration;
        _readStatus = readStatus;
    }

    internal SuiteRuntimeConfiguration Config => _configuration.Current;
    internal FeatureStatusSnapshot Status => _readStatus();
    internal bool IsOn => _configuration.Current.AutoHarvest.Mode == AutoHarvestOperationMode.Active;

    internal void Toggle() => _configuration.ToggleAutoHarvest();
}
