using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoItemsServiceCycleDiagnosticsBridge
{
    private readonly AutoItemsFeatureDependencies _dependencies;
    private readonly AutoItemsConsumableUseGameAction _gameAction;
    private readonly AutoItemsActionHealth _health;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _cycleObserved;
    private AutoItemsDecisionKind _decisionKind;
    private long _publishedHealthRevision = -1;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];

    internal AutoItemsServiceCycleDiagnosticsBridge(
        AutoItemsFeatureDependencies dependencies,
        AutoItemsConsumableUseGameAction gameAction,
        AutoItemsActionHealth health,
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        _dependencies = dependencies;
        _gameAction = gameAction;
        _health = health;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        Publish();
    }

    internal void Observe(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        var changed = _emergencyDisabled != pump.IsEmergencyStopEngaged;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        if (report.ResponsesAcquired != 0 &&
            TryReadLatestDecision(pump, out var decisionKind))
        {
            changed = changed || !_cycleObserved || _decisionKind != decisionKind;
            _cycleObserved = true;
            _decisionKind = decisionKind;
        }
        if (_publishedHealthRevision != _health.Revision)
        {
            _publishedHealthRevision = _health.Revision;
            changed = true;
        }
        if (changed) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _decisionKind = AutoItemsDecisionKind.Disabled;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _decisionKind = AutoItemsDecisionKind.Disabled;
        _health.InvalidateLifecycle();
        _publishedHealthRevision = _health.Revision;
        Publish();
    }

    private void Publish()
    {
        var status = AutoItemsFeatureStatusProjector.Project(
            _emergencyDisabled,
            _dependencies.OwnsActionFamily(),
            _dependencies.ReadOwnershipFailure(),
            _gameAction.BindingsAvailable,
            _gameAction.BindingFailure,
            _cycleObserved,
            _decisionKind,
            _health);
        _dependencies.FeatureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private bool TryReadLatestDecision(
        SuiteFramePump pump,
        out AutoItemsDecisionKind decisionKind)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            var service = _services[index];
            if (!service.ServiceId.Equals(AutoItemsServicePolicies.ServiceId) ||
                !service.LatestProjection.IsPresent)
                continue;
            var projection = service.LatestProjection.Snapshot;
            return AutoItemsServiceProjection.TryReadDecisionKind(
                in projection,
                out decisionKind);
        }
        decisionKind = AutoItemsDecisionKind.Disabled;
        return false;
    }
}
