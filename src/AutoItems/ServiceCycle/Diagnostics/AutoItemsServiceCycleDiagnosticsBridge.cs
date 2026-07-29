using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoItemsServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;

    internal AutoItemsServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
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

    internal void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        _cycleObserved = false;
        Publish();
    }

    private void Publish()
    {
        var status = AutoItemsFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved);
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }
}
