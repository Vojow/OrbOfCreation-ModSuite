using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoConceptServiceCycleDiagnosticsBridge
{
    private readonly AutomataFeatureStatusReporter _featureStatus;
    private long _lifecycle;
    private ConfigGeneration _configurationGeneration;
    private bool _emergencyDisabled;
    private bool _owned;
    private bool _cycleObserved;
    private AutoConceptIdleReason _idleReason;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];

    internal AutoConceptServiceCycleDiagnosticsBridge(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned,
        AutomataFeatureStatusReporter featureStatus)
    {
        _featureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
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
        if (report.ResponsesAcquired != 0 &&
            TryReadLatestDecision(pump, out var idleReason))
        {
            changed = changed || !_cycleObserved || _idleReason != idleReason;
            _cycleObserved = true;
            _idleReason = idleReason;
        }
        if (changed) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _configurationGeneration = configurationGeneration;
        _cycleObserved = false;
        _idleReason = AutoConceptIdleReason.None;
    }

    internal void ObserveLifecycle(
        long lifecycle,
        ConfigGeneration configurationGeneration,
        bool owned)
    {
        if (configurationGeneration.Value < _configurationGeneration.Value) return;
        _lifecycle = lifecycle;
        _configurationGeneration = configurationGeneration;
        _owned = owned;
        _cycleObserved = false;
        _idleReason = AutoConceptIdleReason.None;
        Publish();
    }

    private void Publish()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            _emergencyDisabled,
            _owned,
            _cycleObserved,
            _idleReason);
        _featureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configurationGeneration);
    }

    private bool TryReadLatestDecision(
        SuiteFramePump pump,
        out AutoConceptIdleReason idleReason)
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
            if (!service.ServiceId.Equals(AutoConceptServicePolicies.ServiceId) ||
                !service.LatestProjection.IsPresent) continue;
            var projection = service.LatestProjection.Snapshot;
            for (var entryIndex = 0; entryIndex < projection.Count; entryIndex++)
            {
                var entry = projection.GetEntry(entryIndex);
                if (entry.Key.Value != AutoConceptServiceProjection.IdleReasonKey ||
                    entry.Value.Kind != ServiceProjectionValueKind.Integer ||
                    entry.Value.Integer is < int.MinValue or > int.MaxValue) continue;
                var value = (int)entry.Value.Integer;
                if (!Enum.IsDefined(typeof(AutoConceptIdleReason), value)) break;
                idleReason = (AutoConceptIdleReason)value;
                return true;
            }
        }

        idleReason = AutoConceptIdleReason.None;
        return false;
    }

}
