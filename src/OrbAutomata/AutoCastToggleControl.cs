using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoCastToggleVisualState
{
    Off,
    On,
}

internal sealed class AutoCastToggleControl
{
    private readonly IAutomataConfigurationEditor _config;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;

    public AutoCastToggleControl(IAutomataConfigurationEditor config, System.Func<FeatureStatusSnapshot>? readStatus = null)
    {
        _config = config;
        _readStatus = readStatus;
    }

    internal AutomataConfiguration Config => _config.Current;
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();

    public AutoCastToggleVisualState State
    {
        get
        {
            return AutomataFeatureStatusVisuals.ToVisualState(Status);
        }
    }

    public void Toggle()
    {
        _config.ToggleAutoCast();
    }

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = Config.AutoCast.Mode == AutoCastOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.AutoCastFeatureId),
            "Auto Cast",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !Config.CanStartAutoCastActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Cast is disabled by configuration.")
                : !Config.CanStartAutoCastActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }
}
