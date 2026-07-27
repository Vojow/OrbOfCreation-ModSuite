using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoHarvestServiceCycleDiagnosticsBridge : IDisposable
{
    internal const string ImplementationName = "ServiceCycle";

    private readonly AutomataFeatureStatusReporter? _featureStatus;
    // Sized for the Automata host's registered services so the ordinary frame copies nothing extra and
    // allocates nothing; a host that grows past it resizes once and carries on.
    private ServiceCycleServiceDiagnosticsSnapshot[] _services = new ServiceCycleServiceDiagnosticsSnapshot[4];
    private readonly AutoHarvestRuntimeDiagnosticsPublisher? _runtime;
    private AutoHarvestPairHealth _fruit;
    private AutoHarvestPairHealth _treasure;
    private long _lifecycle;
    private bool _emergencyDisabled;
    private bool _ownsActionFamily;
    private bool _projectionRefreshPending;
    private bool _featureEnabled;

    public AutoHarvestServiceCycleDiagnosticsBridge(
        long lifecycle,
        in SuiteRuntimeConfiguration configuration,
        bool ownsActionFamily,
        RuntimeDiagnosticsRegistry? runtimeDiagnostics,
        AutomataFeatureStatusReporter? featureStatus)
    {
        _featureStatus = featureStatus;
        _lifecycle = lifecycle;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        _featureEnabled = InitialHealth(configuration, out _fruit, out _treasure);
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
        if (!TryReadLatestProjection(pump, out var projection))
        {
            if (conditionsChanged) PublishFeatureStatus();
            return;
        }
        _projectionRefreshPending = false;
        if (!AutoHarvestServiceProjection.TryReadFruitHealth(in projection, out var fruit) ||
            !AutoHarvestServiceProjection.TryReadTreasureHealth(in projection, out var treasure))
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
        in SuiteRuntimeConfiguration configuration,
        bool ownsActionFamily)
    {
        _lifecycle = lifecycle;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        _projectionRefreshPending = false;
        _featureEnabled = InitialHealth(configuration, out _fruit, out _treasure);
        _runtime?.PublishState(lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void ObserveConfiguration(
        in SuiteRuntimeConfiguration configuration,
        bool ownsActionFamily)
    {
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        _ownsActionFamily = ownsActionFamily;
        _projectionRefreshPending = false;
        _featureEnabled = InitialHealth(configuration, out _fruit, out _treasure);
        _runtime?.PublishState(_lifecycle, _fruit, _treasure);
        PublishFeatureStatus();
    }

    public void Dispose() => _runtime?.Dispose();

    /// <summary>
    /// The latest projection Auto Harvest's own service published, if it has published one.
    /// </summary>
    /// <remarks>
    /// By service identity, and against a destination big enough to hold every registered service.
    /// This used to copy into one slot and read ordinal zero, which is two mistakes that hid each
    /// other: the copy was never complete once the host held more than one service, so the read never
    /// happened — and had it happened it would have read the world collection service, which registers
    /// first and knows nothing about harvest pairs. The health line sat on its seeded value for the
    /// whole session, telling players a running feature was still waiting for native evidence.
    /// </remarks>
    private bool TryReadLatestProjection(
        SuiteFramePump pump,
        out ServiceStateProjectionSnapshot projection)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            if (!_services[index].ServiceId.Equals(AutoHarvestServicePolicies.ServiceId)) continue;
            if (!_services[index].LatestProjection.IsPresent) break;
            projection = _services[index].LatestProjection.Snapshot;
            return true;
        }
        projection = default;
        return false;
    }

    private void PublishFeatureStatus()
    {
        if (_featureStatus is null) return;
        var health = AutoHarvestFeatureStatusProjector.Project(_featureEnabled, _fruit, _treasure);
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

    /// <summary>Seeds the pair health and reports whether the player has the feature switched on.</summary>
    private static bool InitialHealth(
        in SuiteRuntimeConfiguration configuration,
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
        return configured;
    }

    private static bool SameHealth(
        in AutoHarvestPairHealth left,
        in AutoHarvestPairHealth right) =>
        left.Pair == right.Pair &&
        left.Selected == right.Selected &&
        left.Kind == right.Kind &&
        left.FeatureScoped == right.FeatureScoped;
}
