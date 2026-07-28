using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoConceptServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter? _featureStatus;
    private long _lifecycle;
    private bool _pluginEnabled;
    private bool _featureEnabled;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;

    internal AutoConceptServiceCycleDiagnosticsBridge(
        long lifecycle,
        SuiteRuntimeConfiguration configuration,
        bool owned,
        AutomataFeatureStatusReporter? featureStatus)
    {
        _featureStatus = featureStatus;
        _lifecycle = lifecycle;
        _owned = owned;
        ReadConfiguration(configuration);
        Publish();
    }

    internal void Observe(SuiteFramePump pump, in SuiteFramePumpReport report, bool owned)
    {
        var changed = _emergencyDisabled != pump.IsEmergencyStopEngaged || _owned != owned;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        if (!_cycleObserved && report.ResponsesAcquired != 0)
        {
            _cycleObserved = true;
            Publish();
        }
        else if (changed)
        {
            Publish();
        }
    }

    internal void ObserveConfiguration(SuiteRuntimeConfiguration configuration, bool owned)
    {
        _owned = owned;
        ReadConfiguration(configuration);
        Publish();
    }

    internal void ObserveLifecycle(
        long lifecycle,
        SuiteRuntimeConfiguration configuration,
        bool owned)
    {
        _lifecycle = lifecycle;
        _owned = owned;
        _cycleObserved = false;
        ReadConfiguration(configuration);
        Publish();
    }

    private void Publish()
    {
        if (_featureStatus is null) return;
        var status = AutoConceptFeatureStatusProjector.Project(
            _pluginEnabled,
            _featureEnabled,
            _emergencyDisabled,
            _owned,
            _cycleObserved);
        _featureStatus.ObserveLifecycle(
            status.State != FeatureStatusState.ConfigurationDisabled,
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle);
    }

    private void ReadConfiguration(SuiteRuntimeConfiguration configuration)
    {
        _pluginEnabled = configuration.General.Enabled;
        _featureEnabled = configuration.AutoConcept.Mode == AutoConceptOperationMode.Active;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
    }
}
