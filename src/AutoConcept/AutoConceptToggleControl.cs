using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptToggleControl
{
    private readonly IAutomataConfigurationEditor _config;
    private readonly System.Func<FeatureStatusSnapshot>? _readStatus;
    private readonly System.Action<SuiteRuntimeConfiguration>? _publishConfiguredIntent;
    public AutoConceptToggleControl(
        IAutomataConfigurationEditor config,
        System.Func<FeatureStatusSnapshot>? readStatus = null,
        System.Action<SuiteRuntimeConfiguration>? publishConfiguredIntent = null)
    {
        _config = config;
        _readStatus = readStatus;
        _publishConfiguredIntent = publishConfiguredIntent;
    }

    internal SuiteRuntimeConfiguration Config => _config.Current;
    internal FeatureStatusSnapshot Status => _readStatus?.Invoke() ?? CreateFallbackStatus();

    public AutoCastToggleVisualState State => AutomataFeatureStatusVisuals.ToVisualState(Status);

    public void Toggle()
    {
        _config.ToggleAutoConcept();
        _publishConfiguredIntent?.Invoke(_config.Current);
    }

    private FeatureStatusSnapshot CreateFallbackStatus()
    {
        var enabled = Config.AutoConcept.Mode == AutoConceptOperationMode.Active;
        return new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoConceptFeatureId),
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
