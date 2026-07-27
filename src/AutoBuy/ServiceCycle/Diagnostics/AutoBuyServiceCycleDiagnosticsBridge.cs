using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common;

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
    private readonly AutomataFeatureStatusReporter? _featureStatus;
    // Sized for the Automata host's registered services so the ordinary frame copies nothing extra and
    // allocates nothing; a host that grows past it resizes once and carries on.
    private ServiceCycleServiceDiagnosticsSnapshot[] _services = new ServiceCycleServiceDiagnosticsSnapshot[4];
    private long _lifecycle;
    private bool _pluginEnabled;
    private bool _featureEnabled;
    private bool _emergencyDisabled;
    private AutoBuyCandidateKinds _selected;
    private AutoBuyCandidateKinds _owned;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;

    public AutoBuyServiceCycleDiagnosticsBridge(
        long lifecycle,
        SuiteRuntimeConfiguration configuration,
        AutoBuyCandidateKinds owned,
        AutomataFeatureStatusReporter? featureStatus)
    {
        _featureStatus = featureStatus;
        _lifecycle = lifecycle;
        _owned = owned;
        ReadConfiguration(configuration);
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

    public void ObserveConfiguration(
        SuiteRuntimeConfiguration configuration,
        AutoBuyCandidateKinds owned)
    {
        _owned = owned;
        ReadConfiguration(configuration);
        PublishFeatureStatus();
    }

    public void ObserveLifecycle(
        long lifecycle,
        SuiteRuntimeConfiguration configuration,
        AutoBuyCandidateKinds owned)
    {
        _lifecycle = lifecycle;
        _owned = owned;
        // A lifecycle boundary retires the worker state the previous generation evaluated against, so
        // the feature is waiting on a first evaluation again.
        _cycleObserved = false;
        _evaluationRefreshPending = false;
        ReadConfiguration(configuration);
        PublishFeatureStatus();
    }

    private void PublishFeatureStatus()
    {
        if (_featureStatus is null) return;
        var health = AutoBuyFeatureStatusProjector.Project(
            _pluginEnabled,
            _featureEnabled,
            _emergencyDisabled,
            _selected,
            _owned,
            _cycleObserved,
            RetainedStandDownSummary());
        _featureStatus.ObserveLifecycle(
            health.State != FeatureStatusState.ConfigurationDisabled,
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle);
    }

    /// <summary>
    /// The refusal responder's account of why it turned Auto Buy off, if that is why Auto Buy is off.
    /// It is read back from the status it published rather than tracked separately, so there is one
    /// record of the stand-down and no second one to disagree with it.
    /// </summary>
    private string? RetainedStandDownSummary()
    {
        if (_featureStatus is null) return null;
        var current = _featureStatus.Current;
        return !current.ConfiguredEnabled &&
               current.Reason.Code == FeatureStatusReasonCode.InvariantViolation
            ? current.Reason.Summary
            : null;
    }

    private void ReadConfiguration(SuiteRuntimeConfiguration configuration)
    {
        _pluginEnabled = configuration.General.Enabled;
        _featureEnabled = configuration.AutoBuy.Mode == AutoBuyOperationMode.Active;
        _emergencyDisabled = configuration.Safety.EmergencyDisable;
        var selected = AutoBuyCandidateKinds.None;
        if (configuration.AutoBuy.IncludeStructures) selected |= AutoBuyCandidateKinds.Structures;
        if (configuration.AutoBuy.IncludeUpgrades) selected |= AutoBuyCandidateKinds.Upgrades;
        _selected = selected;
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
