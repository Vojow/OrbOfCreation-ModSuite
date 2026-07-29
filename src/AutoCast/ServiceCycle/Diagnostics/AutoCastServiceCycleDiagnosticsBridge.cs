using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

/// <summary>
/// Keeps the Auto Cast feature-status line describing the running feature, which is what the toggle
/// button's tooltip reads.
/// </summary>
/// <remarks>
/// Everything the line reports — ownership, the emergency stop, the manual pause — is main-thread
/// state, which is why it is written here rather than by the worker. The pause is read per frame
/// rather than latched, because it is the one blocking term that expires without anybody doing
/// anything, and a line that only updated when something happened would keep claiming a pause that
/// ran out while the player stood still.
/// </remarks>
internal sealed class AutoCastServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly AutoCastManualPauseState _manualPause;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _manualPaused;
    private bool _cycleObserved;

    public AutoCastServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        AutoCastManualPauseState manualPause,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _manualPause = manualPause;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        PublishFeatureStatus();
    }

    public void Observe(SuiteFramePump pump, in SuiteFramePumpReport report, bool owned)
    {
        var paused = _manualPause.IsPaused(pump.DiagnosticsNow);
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned ||
            _manualPaused != paused;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        _manualPaused = paused;
        if (!_cycleObserved && report.ResponsesAcquired != 0)
        {
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
    }

    public void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        // Nothing survives a lifecycle boundary. A pause earned in the previous run of the game is
        // not a fact about this one, and the worker has not evaluated against the new world yet.
        _manualPause.Reset();
        _manualPaused = false;
        _cycleObserved = false;
        PublishFeatureStatus();
    }

    private void PublishFeatureStatus()
    {
        var health = AutoCastFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _manualPaused,
            _cycleObserved);
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }

}
