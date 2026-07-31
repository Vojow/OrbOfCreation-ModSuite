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
    private Guid _quarantinedTemporaryItem;
    private AutoItemsTemporaryQuarantineCause _temporaryQuarantineCause;
    private Guid _quarantinedPermanentItem;
    private AutoItemsPermanentQuarantineCause _permanentQuarantineCause;
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
            TryReadLatestDecision(
                pump,
                out var decisionKind,
                out var quarantinedTemporaryItem,
                out var temporaryQuarantineCause,
                out var quarantinedPermanentItem,
                out var permanentQuarantineCause))
        {
            changed = changed ||
                !_cycleObserved ||
                _decisionKind != decisionKind ||
                _quarantinedTemporaryItem != quarantinedTemporaryItem ||
                _temporaryQuarantineCause != temporaryQuarantineCause ||
                _quarantinedPermanentItem != quarantinedPermanentItem ||
                _permanentQuarantineCause != permanentQuarantineCause;
            _cycleObserved = true;
            _decisionKind = decisionKind;
            _quarantinedTemporaryItem = quarantinedTemporaryItem;
            _temporaryQuarantineCause = temporaryQuarantineCause;
            _quarantinedPermanentItem = quarantinedPermanentItem;
            _permanentQuarantineCause = permanentQuarantineCause;
            if (decisionKind is not AutoItemsDecisionKind.Relic and
                not AutoItemsDecisionKind.Scroll and
                not AutoItemsDecisionKind.TemporaryItem)
                changed |= _health.ClearTransient();
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
        _quarantinedTemporaryItem = Guid.Empty;
        _temporaryQuarantineCause = AutoItemsTemporaryQuarantineCause.None;
        _quarantinedPermanentItem = Guid.Empty;
        _permanentQuarantineCause = AutoItemsPermanentQuarantineCause.None;
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
        _quarantinedTemporaryItem = Guid.Empty;
        _temporaryQuarantineCause = AutoItemsTemporaryQuarantineCause.None;
        _quarantinedPermanentItem = Guid.Empty;
        _permanentQuarantineCause = AutoItemsPermanentQuarantineCause.None;
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
            _quarantinedTemporaryItem,
            _temporaryQuarantineCause,
            _quarantinedPermanentItem,
            _permanentQuarantineCause,
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
        out AutoItemsDecisionKind decisionKind,
        out Guid quarantinedTemporaryItem,
        out AutoItemsTemporaryQuarantineCause temporaryQuarantineCause,
        out Guid quarantinedPermanentItem,
        out AutoItemsPermanentQuarantineCause permanentQuarantineCause)
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
            return AutoItemsServiceProjection.TryReadDecision(
                in projection,
                out decisionKind,
                out quarantinedTemporaryItem,
                out temporaryQuarantineCause,
                out quarantinedPermanentItem,
                out permanentQuarantineCause);
        }
        decisionKind = AutoItemsDecisionKind.Disabled;
        quarantinedTemporaryItem = Guid.Empty;
        temporaryQuarantineCause = AutoItemsTemporaryQuarantineCause.None;
        quarantinedPermanentItem = Guid.Empty;
        permanentQuarantineCause = AutoItemsPermanentQuarantineCause.None;
        return false;
    }
}
