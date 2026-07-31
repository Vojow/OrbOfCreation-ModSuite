using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoScribeToggleControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly Func<FeatureStatusSnapshot> _readStatus;

    internal AutoScribeToggleControl(
        AutomataConfigurationStore configuration,
        Func<FeatureStatusSnapshot> readStatus)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
    }

    internal SuiteRuntimeConfiguration Config => _configuration.Current;
    internal FeatureStatusSnapshot Status => _readStatus();
    internal bool IsOn => Config.AutoScribe.Mode == AutoScribeOperationMode.Active;

    internal void Toggle() => _configuration.ToggleAutoScribe();
}
