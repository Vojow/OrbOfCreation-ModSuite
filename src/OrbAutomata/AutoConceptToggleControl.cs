using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptToggleControl
{
    private readonly IAutomataConfigurationEditor _config;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;
    public AutoConceptToggleControl(IAutomataConfigurationEditor config, System.Func<FeatureStatusSnapshot>? readStatus = null)
    {
        _config = config;
        _readStatus = readStatus;
    }

    internal AutomataConfiguration Config => _config.Current;
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();

    public AutoCastToggleVisualState State => AutomataFeatureStatusVisuals.ToVisualState(Status);

    public void Toggle() => _config.ToggleAutoConcept();

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = Config.AutoConcept.Mode == AutoConceptOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.AutomataGuid, AutomataFeatureStatuses.AutoConceptFeatureId),
            "Auto Concept",
            enabled,
            !enabled
                ? FeatureStatusState.ConfigurationDisabled
                : !Config.CanStartAutoConceptActively
                    ? FeatureStatusState.TemporarilyBlocked
                    : FeatureStatusState.Operational,
            !enabled
                ? new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "Auto Concept is disabled by configuration.")
                : !Config.CanStartAutoConceptActively
                    ? new FeatureStatusReason(FeatureStatusReasonCode.EmergencyDisabled, "Automata Emergency Disable is active.")
                    : default);
    }
}
