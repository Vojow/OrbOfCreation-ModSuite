using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoHarvestServiceCycleDiagnosticsBridge : IDisposable
{
    internal const string ImplementationName = "ServiceCycle";

    private readonly AutomataFeatureStatusReporter? _featureStatus;
    private readonly ServiceCycleServiceDiagnosticsSnapshot[] _service = new ServiceCycleServiceDiagnosticsSnapshot[1];
    private readonly AutoHarvestRuntimeDiagnosticsPublisher? _runtime;
    private AutoHarvestPairHealth _fruit;
    private AutoHarvestPairHealth _treasure;
    private long _lifecycle;
    private bool _emergencyDisabled;
    private bool _ownsActionFamily;
    private bool _projectionRefreshPending;

    public AutoHarvestServiceCycleDiagnosticsBridge(
        long lifecycle,
        in AutomataConfiguration configuration,
        bool ownsActionFamily,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics,
        AutomataFeatureStatusReporter? featureStatus)
    {
        _featureStatus = featureStatus;
        _lifecycle = lifecycle;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        InitialHealth(configuration, out _fruit, out _treasure);
        if (runtimeDiagnostics is not null)
        {
            _runtime = new AutoHarvestRuntimeDiagnosticsPublisher(
                lifecycle,
                _fruit,
                _treasure,
                ImplementationName,
                runtimeDiagnostics);
        }
        PublishFeatureStatus();
    }

    public void Observe(
        SuiteFramePump pump,
        in SuiteFramePumpReport report,
        bool ownsActionFamily)
    {
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _ownsActionFamily != ownsActionFamily;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _ownsActionFamily = ownsActionFamily;
        if (report.ResponsesAcquired != 0) _projectionRefreshPending = true;
        if (!_projectionRefreshPending)
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _service);
        if (!copy.IsComplete || !_service[0].LatestProjection.IsPresent)
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _projectionRefreshPending = false;
        if (!AutoHarvestServiceProjection.TryReadFruitHealth(
                _service[0].LatestProjection.Snapshot,
                out var fruit) ||
            !AutoHarvestServiceProjection.TryReadTreasureHealth(
                _service[0].LatestProjection.Snapshot,
                out var treasure))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        if (SameHealth(_fruit, fruit) && SameHealth(_treasure, treasure))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _fruit = fruit;
        _treasure = treasure;
        _runtime?.PublishState(_lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void ObserveLifecycle(
        long lifecycle,
        in AutomataConfiguration configuration,
        bool ownsActionFamily)
    {
        _lifecycle = lifecycle;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        _projectionRefreshPending = false;
        InitialHealth(configuration, out _fruit, out _treasure);
        _runtime?.PublishState(lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void ObserveConfiguration(
        in AutomataConfiguration configuration,
        bool ownsActionFamily)
    {
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        _projectionRefreshPending = false;
        InitialHealth(configuration, out _fruit, out _treasure);
        _runtime?.PublishState(_lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void Dispose() => _runtime?.Dispose();

    private void PublishFeatureStatus()
    {
        if (_featureStatus is null) return;
        var health = AutoHarvestFeatureStatusProjector.Project(_fruit, _treasure);
        if (health.State != FeatureStatusState.ConfigurationDisabled && _emergencyDisabled)
        {
            _featureStatus.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.EmergencyDisabled,
                "Emergency disable blocks Auto Harvest.",
                _lifecycle);
            return;
        }
        if (health.State != FeatureStatusState.ConfigurationDisabled && !_ownsActionFamily)
        {
            _featureStatus.ObserveLifecycle(
                true,
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Harvest action-family ownership is unavailable.",
                _lifecycle);
            return;
        }
        _featureStatus.ObserveLifecycle(
            health.State != FeatureStatusState.ConfigurationDisabled,
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle);
    }

    private static void InitialHealth(
        in AutomataConfiguration configuration,
        out AutoHarvestPairHealth fruit,
        out AutoHarvestPairHealth treasure)
    {
        var configured = configuration.General.Enabled && configuration.AutoHarvest.Mode == AutoHarvestOperationMode.Active;
        fruit = configured && configuration.AutoHarvest.CollectFruitTrees
            ? AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree)
            : AutoHarvestPairHealth.NotSelected(AutoHarvestPair.FruitTree);
        treasure = configured && configuration.AutoHarvest.CollectTreasureTrees
            ? AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree)
            : AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);
    }

    private static bool SameHealth(
        in AutoHarvestPairHealth left,
        in AutoHarvestPairHealth right) =>
        left.Pair == right.Pair &&
        left.Selected == right.Selected &&
        left.Kind == right.Kind &&
        left.FeatureScoped == right.FeatureScoped;
}
