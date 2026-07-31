using System;

namespace OrbAutomata;

internal sealed class EmergencyStopControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly Action<bool> _changed;

    public EmergencyStopControl(
        AutomataConfigurationStore configuration,
        Action<bool> changed)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public bool IsStopped => _configuration.Current.Safety.EmergencyDisable;
    public string Label => IsStopped ? "STOPPED" : "STOP ALL";

    public void Activate()
    {
        var stopped = !IsStopped;
        _changed(stopped);
        _configuration.SetEmergencyStop(stopped);
    }
}
