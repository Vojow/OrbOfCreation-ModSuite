using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoScribeServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _owned;
    private bool _canConsumeScrolls;
    private bool _emergencyDisabled;
    private bool _cycleObserved;
    private bool _quarantined;
    private int _unknownRoles;

    internal AutoScribeServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        bool canConsumeScrolls,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        _canConsumeScrolls = canConsumeScrolls;
        Publish();
    }

    internal void Observe(
        SuiteFramePump pump,
        in SuiteFramePumpReport report,
        bool owned,
        bool canConsumeScrolls,
        bool quarantined)
    {
        var changed =
            _emergencyDisabled != pump.IsEmergencyStopEngaged ||
            _owned != owned ||
            _canConsumeScrolls != canConsumeScrolls ||
            _quarantined != quarantined;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        _owned = owned;
        _canConsumeScrolls = canConsumeScrolls;
        _quarantined = quarantined;
        if (TryReadProjection(pump, out var unknownRoles))
        {
            changed |= !_cycleObserved || _unknownRoles != unknownRoles;
            _cycleObserved = true;
            _unknownRoles = unknownRoles;
        }
        if (changed || report.ResponsesAcquired != 0) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration generation)
    {
        if (generation.Value < _configurationGeneration.Value) return;
        _configurationGeneration = generation;
        _cycleObserved = false;
        _unknownRoles = 0;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration generation,
        bool owned,
        bool canConsumeScrolls)
    {
        if (generation.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = generation;
        _owned = owned;
        _canConsumeScrolls = canConsumeScrolls;
        _cycleObserved = false;
        _quarantined = false;
        _unknownRoles = 0;
        Publish();
    }

    private void Publish()
    {
        var status = Project();
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private AutoScribeFeatureStatus Project()
    {
        if (_emergencyDisabled)
            return AutoScribeFeatureStatus.Blocked(
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        if (!_owned)
            return AutoScribeFeatureStatus.Blocked(
                FeatureStatusReasonCode.ActionFamilyConflict,
                "Another automation owner holds Scribe queue submission.");
        if (!_canConsumeScrolls)
            return AutoScribeFeatureStatus.Blocked(
                FeatureStatusReasonCode.ParentFeatureDisabled,
                "Auto Scribe is waiting for healthy Auto Items Scroll consumption.");
        if (_quarantined)
            return AutoScribeFeatureStatus.Faulted(
                FeatureStatusReasonCode.PostconditionFailed,
                "Auto Scribe is quarantined until the gameplay lifecycle changes.");
        if (!_cycleObserved)
            return AutoScribeFeatureStatus.NotReady(
                "Auto Scribe is waiting for its first coverage evaluation.");
        if (_unknownRoles > 0)
            return AutoScribeFeatureStatus.Degraded(
                $"{_unknownRoles} Auto Scribe role(s) lack complete native coverage evidence.");
        return AutoScribeFeatureStatus.Operational();
    }

    private bool TryReadProjection(SuiteFramePump pump, out int unknownRoles)
    {
        unknownRoles = 0;
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            var service = _services[index];
            if (service.ServiceId != AutoScribeServiceCycleFeature.ServiceId ||
                !service.LatestProjection.IsPresent)
            {
                continue;
            }
            var snapshot = service.LatestProjection.Snapshot;
            for (var entryIndex = 0; entryIndex < snapshot.Count; entryIndex++)
            {
                var entry = snapshot.GetEntry(entryIndex);
                if (entry.Key.Value == 13 &&
                    entry.Value.Kind == ServiceProjectionValueKind.Integer)
                {
                    unknownRoles = checked((int)entry.Value.Integer);
                    return true;
                }
            }
            return true;
        }
        return false;
    }

    private readonly struct AutoScribeFeatureStatus
    {
        private AutoScribeFeatureStatus(
            FeatureStatusState state,
            FeatureStatusReasonCode reason,
            string summary)
        {
            State = state;
            Reason = reason;
            Summary = summary;
        }

        internal FeatureStatusState State { get; }
        internal FeatureStatusReasonCode Reason { get; }
        internal string Summary { get; }

        internal static AutoScribeFeatureStatus Operational() =>
            new(FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty);
        internal static AutoScribeFeatureStatus NotReady(string summary) =>
            new(FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady, summary);
        internal static AutoScribeFeatureStatus Blocked(
            FeatureStatusReasonCode reason,
            string summary) =>
            new(FeatureStatusState.TemporarilyBlocked, reason, summary);
        internal static AutoScribeFeatureStatus Degraded(string summary) =>
            new(FeatureStatusState.Degraded, FeatureStatusReasonCode.EvidenceUnavailable, summary);
        internal static AutoScribeFeatureStatus Faulted(
            FeatureStatusReasonCode reason,
            string summary) =>
            new(FeatureStatusState.Faulted, reason, summary);
    }
}
