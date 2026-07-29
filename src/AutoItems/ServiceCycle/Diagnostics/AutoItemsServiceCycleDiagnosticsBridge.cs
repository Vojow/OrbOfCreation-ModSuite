using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoItemsServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;

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
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        if (!_cycleObserved && _evaluationRefreshPending && HasEvaluated(pump))
        {
            _evaluationRefreshPending = false;
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
        _evaluationRefreshPending = false;
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
        _evaluationRefreshPending = false;
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

    private bool HasEvaluated(SuiteFramePump pump)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            if (!_services[index].ServiceId.Equals(AutoItemsServicePolicies.ServiceId)) continue;
            return _services[index].LatestProjection.IsPresent;
        }
        return false;
    }
}
