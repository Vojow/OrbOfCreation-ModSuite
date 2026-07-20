using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoCastToggleVisualState
{
    Off,
    On,
}

internal sealed class AutoCastToggleControl
{
    private readonly AutomataConfig _config;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;

    public AutoCastToggleControl(AutomataConfig config, System.Func<FeatureStatusSnapshot>? readStatus = null)
    {
        _config = config;
        _readStatus = readStatus;
    }

    internal AutomataConfig Config => _config;
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
        _config.AutoCastMode.Value = _config.AutoCastMode.Value == AutoCastOperationMode.Active
            ? AutoCastOperationMode.Disabled
            : AutoCastOperationMode.Active;
    }

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = _config.AutoCastMode.Value == AutoCastOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.AutoCastFeatureId),
            "Auto Cast",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !_config.CanStartAutoCastActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Cast is disabled by configuration.")
                : !_config.CanStartAutoCastActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }
}
