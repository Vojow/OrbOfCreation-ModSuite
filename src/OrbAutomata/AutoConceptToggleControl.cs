using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptToggleControl
{
    private readonly AutomataConfig _config;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;
    public AutoConceptToggleControl(AutomataConfig config, System.Func<FeatureStatusSnapshot>? readStatus = null)
    {
        _config = config;
        _readStatus = readStatus;
    }

    internal AutomataConfig Config => _config;
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();

    public AutoCastToggleVisualState State => AutomataFeatureStatusVisuals.ToVisualState(Status);

    public void Toggle() =>
        _config.AutoConceptMode.Value = _config.AutoConceptMode.Value == AutoConceptOperationMode.Active
            ? AutoConceptOperationMode.Disabled
            : AutoConceptOperationMode.Active;

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = _config.AutoConceptMode.Value == AutoConceptOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.AutoConceptFeatureId),
            "Auto Concept",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !_config.CanStartAutoConceptActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Concept is disabled by configuration.")
                : !_config.CanStartAutoConceptActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }
}
