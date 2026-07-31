using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

/// <summary>
/// Keeps the Spell Leveling feature-status line describing the running feature, and seeds the
/// capability and ordinary boundary wait state the toggle-button tooltip reads.
/// </summary>
/// <remarks>
/// Spell Leveling has no button of its own; Auto Buy's tooltip carries its line. That makes the status
/// registry the only thing standing between the player and a stale claim, and it is written here
/// rather than by the worker because everything it reports — ownership, emergency stop, progression,
/// and the last live affordability refusal — is main-thread state.
/// <para>
/// The capability probe runs once per lifecycle, on the first cycle the service completes, and never
/// per frame. Before it runs the capability reads <see cref="AutoSpellLevelCapability.Locked"/>, which
/// is why the projector will not report progression until a cycle has been observed: unknown and
/// locked must not look alike.
/// </para>
/// </remarks>
internal sealed class SpellLevelServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private readonly SpellLevelCapabilityState _capability;
    private readonly SpellLevelBoundaryStatusState _boundaryStatus;
    private readonly ISpellLevelCapabilityPort _capabilityPort;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;
    private bool _evaluationRefreshPending;
    private AutoSpellLevelCapability _reportedCapability;
    private string? _reportedWaitingReason;

    public SpellLevelServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        SpellLevelCapabilityState capability,
        SpellLevelBoundaryStatusState boundaryStatus,
        ISpellLevelCapabilityPort capabilityPort,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _boundaryStatus = boundaryStatus ?? throw new ArgumentNullException(nameof(boundaryStatus));
        _capabilityPort = capabilityPort ?? throw new ArgumentNullException(nameof(capabilityPort));
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        PublishFeatureStatus();
    }

    public void Observe(SuiteFramePump pump, in SuiteFramePumpReport report, bool owned)
    {
        var conditionsChanged =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned ||
            _reportedCapability != _capability.Current ||
            !string.Equals(_reportedWaitingReason, _boundaryStatus.WaitingReason, StringComparison.Ordinal);
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        if (report.ResponsesAcquired != 0) _evaluationRefreshPending = true;
        if (!_cycleObserved && _evaluationRefreshPending)
        {
            _evaluationRefreshPending = false;
            _cycleObserved = true;
            SeedCapability();
            PublishFeatureStatus();
            return;
        }

        if (conditionsChanged) PublishFeatureStatus();
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _boundaryStatus.Reset();
        _cycleObserved = false;
        _evaluationRefreshPending = false;
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
        // Nothing survives a lifecycle boundary. The capability the last generation reached says
        // nothing about this one, and the worker has not evaluated against the new world yet.
        _capability.Reset();
        _boundaryStatus.Reset();
        _cycleObserved = false;
        _evaluationRefreshPending = false;
        PublishFeatureStatus();
    }

    /// <summary>
    /// Asks the game what the feature can do, once, so the tooltip is right before the first action
    /// rather than after it. An unreadable contract leaves the capability where it was — the status
    /// line already reports a contract that will not bind.
    /// </summary>
    private void SeedCapability()
    {
        if (_capabilityPort.TryReadCapability(out var capability)) _capability.Observe(capability);
    }

    private void PublishFeatureStatus()
    {
        _reportedCapability = _capability.Current;
        _reportedWaitingReason = _boundaryStatus.WaitingReason;
        var health = SpellLevelFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved,
            _reportedCapability,
            _reportedWaitingReason);
        _featureStatus.ObserveRuntimeLifecycle(
            health.State,
            health.Reason,
            health.Summary,
            _lifecycle,
            _configurationGeneration);
    }

}
