using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoAgromancyServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _projectionObserved;
    private AutoAgromancyDecisionKind _decision;
    private int _plannedActions;
    private bool _faulted;
    private bool _projectionRefreshPending;

    internal AutoAgromancyServiceCycleDiagnosticsBridge(
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
        if (report.ResponsesAcquired != 0) _projectionRefreshPending = true;
        if (_projectionRefreshPending &&
            TryReadLatestProjection(
                pump,
                out var projection,
                out var faulted) &&
            AutoAgromancyServiceProjection.TryReadDecision(
                in projection,
                out var decision,
                out var plannedActions))
        {
            _projectionRefreshPending = false;
            _projectionObserved = true;
            _decision = decision;
            _plannedActions = plannedActions;
            _faulted = faulted;
            Publish();
        }
        else if (changed)
        {
            Publish();
        }
    }

    internal void ObserveConfiguration(ConfigGeneration generation)
    {
        if (generation.Value < _configurationGeneration.Value) return;
        _configurationGeneration = generation;
        ResetProjection();
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration generation,
        bool owned)
    {
        if (generation.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = generation;
        _owned = owned;
        ResetProjection();
        Publish();
    }

    private bool TryReadLatestProjection(
        SuiteFramePump pump,
        out ServiceStateProjectionSnapshot projection,
        out bool faulted)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services =
                new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            ref readonly var service = ref _services[index];
            if (!service.ServiceId.Equals(AutoAgromancyServicePolicies.ServiceId))
                continue;
            if (!service.LatestProjection.IsPresent) break;
            projection = service.LatestProjection.Snapshot;
            faulted =
                service.LatestFault.IsValid ||
                service.LastAction.IsPresent &&
                service.LastAction.Result.Disposition ==
                    ServiceActionDisposition.Faulted;
            return true;
        }
        projection = default;
        faulted = false;
        return false;
    }

    private void ResetProjection()
    {
        _projectionObserved = false;
        _decision = default;
        _plannedActions = 0;
        _faulted = false;
        _projectionRefreshPending = false;
    }

    private void Publish()
    {
        var status = AutoAgromancyFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _projectionObserved,
            _decision,
            _plannedActions,
            _faulted);
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }
}
