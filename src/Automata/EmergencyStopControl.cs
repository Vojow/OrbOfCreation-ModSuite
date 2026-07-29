using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata;

internal sealed class EmergencyStopControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly Func<IReadOnlyList<string>> _readResumePreview;
    private readonly Action<bool> _changed;
    private bool _resumeArmed;

    public EmergencyStopControl(
        AutomataConfigurationStore configuration,
        Func<IReadOnlyList<string>> readResumePreview,
        Action<bool> changed)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _readResumePreview = readResumePreview ?? throw new ArgumentNullException(nameof(readResumePreview));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public bool IsStopped => _configuration.Current.Safety.EmergencyDisable;
    public bool ResumeArmed => IsStopped && _resumeArmed;

    public string Label => !IsStopped ? "STOP ALL" : ResumeArmed ? "RESUME?" : "STOPPED";

    public string ResumePreview
    {
        get
        {
            var services = _readResumePreview();
            return services.Count == 0
                ? "No services are configured to resume."
                : "Will resume: " + string.Join(", ", services.Distinct());
        }
    }

    public void Activate()
    {
        if (!IsStopped)
        {
            _resumeArmed = false;
            _changed(true);
            _configuration.SetEmergencyStop(true);
            return;
        }
        if (!_resumeArmed)
        {
            _resumeArmed = true;
            return;
        }
        _resumeArmed = false;
        _changed(false);
        _configuration.SetEmergencyStop(false);
    }

    public void Synchronize()
    {
        if (!IsStopped) _resumeArmed = false;
    }
}
