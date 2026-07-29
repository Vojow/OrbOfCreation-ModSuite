using System;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Keeps the Auto Buy feature-status line describing the running feature: what the player configured,
/// what the suite is allowed to do, and whether the service has actually evaluated yet.
/// </summary>
/// <remarks>
/// The decision journal records every cycle, but nothing in the UI reads it. The button, its tooltip,
/// and the Mod Config health row all read the feature status registry, so a mode changed mid-session
/// is invisible until something republishes here.
/// </remarks>
internal sealed class AutoBuyServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    // Sized for the Automata host's registered services so the ordinary frame copies nothing extra and
    // allocates nothing; a host that grows past it resizes once and carries on.
    private ServiceCycleServiceDiagnosticsSnapshot[] _services = new ServiceCycleServiceDiagnosticsSnapshot[4];
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private AutoBuyCandidateKinds _owned;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;

    public AutoBuyServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        AutoBuyCandidateKinds owned,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        PublishFeatureStatus();
    }

    public void Observe(
        SuiteFramePump pump,
        in SuiteFramePumpReport report,
        AutoBuyCandidateKinds owned)
    {
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        if (!_cycleObserved && _evaluationRefreshPending && HasEvaluated(pump))
        {
            _evaluationRefreshPending = false;
            _cycleObserved = true;
            PublishFeatureStatus();
            return;
        }
        if (conditionsChanged) PublishFeatureStatus();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _evaluationRefreshPending = false;
    }

    public void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        AutoBuyCandidateKinds owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        // A lifecycle boundary retires the worker state the previous generation evaluated against, so
        // the feature is waiting on a first evaluation again.
        _cycleObserved = false;
        _evaluationRefreshPending = false;
        PublishFeatureStatus();
    }

    private void PublishFeatureStatus()
    {
        var health = AutoBuyFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved);
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
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
            if (!_services[index].ServiceId.Equals(AutoBuyServicePolicies.ServiceId)) continue;
            return _services[index].LatestProjection.IsPresent;
        }
        return false;
    }
}
