using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata;

internal sealed class EmergencyStopControl
{
    private readonly IAutomataConfigurationEditor _configuration;
    private readonly Func<IReadOnlyList<string>> _readResumePreview;
    private readonly Action<bool> _changed;
    private bool _resumeArmed;

    public EmergencyStopControl(
        IAutomataConfigurationEditor configuration,
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
            _configuration.SetEmergencyStop(true);
            _changed(true);
            return;
        }
        if (!_resumeArmed)
        {
            _resumeArmed = true;
            return;
        }
        _resumeArmed = false;
        _configuration.SetEmergencyStop(false);
        _changed(false);
    }

    public void Synchronize()
    {
        if (!IsStopped) _resumeArmed = false;
    }
}
