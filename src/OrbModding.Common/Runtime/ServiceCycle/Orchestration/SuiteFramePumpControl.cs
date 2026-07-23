using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpControl
{
    private readonly SuiteFramePumpState _state;

    internal SuiteFramePumpControl(SuiteFramePumpState state) => _state = state;

    internal bool EffectiveEmergencyStop => _state.Emergency.IsEffective;
    internal EmergencyStopContext EffectiveEmergencyContext =>
        _state.Emergency.EffectiveContext;

    internal bool RequestLifecycleReplacement(LifecycleGeneration generation)
    {
        _state.EnsureAvailable();
        var cancelsInFlightCycle = false;
        if (_state.Traces.HasReplay)
        {
            for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
            {
                if (_state.Registry.GetSlot(ordinal).IsBetweenCycles) continue;
                cancelsInFlightCycle = true;
                break;
            }
        }

        var accepted = _state.Registry.RequestLifecycle(generation);
        if (!accepted) return false;
        if (cancelsInFlightCycle)
            _state.Traces.InvalidateReplay();
        var observedAt = _state.Clock.Now;
        for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
            _state.EvidenceEmitter.LifecycleRequested(ordinal, generation, observedAt);
        if (!_state.IsInsideServiceCallback)
        {
            _state.Registry.ReconcileLifecycle(observedAt);
            _state.EvidenceScanner.ObserveLifecycle(
                _state.Traces.Dispatch,
                _state.Journal.Observer,
                observedAt);
        }
        return true;
    }

    internal void SetEmergencyStop(bool engaged, EmergencyStopReason reason)
    {
        _state.EnsureAvailable();
        if (reason is < EmergencyStopReason.UserRequested or > EmergencyStopReason.SuiteShutdown)
            throw new ArgumentOutOfRangeException(nameof(reason));
        var wasEngaged = _state.Emergency.IsEngaged;
        var priorActive = _state.Emergency.Active;
        _state.Emergency.Set(engaged, reason, _state.IsPumping);
        var observedAt = _state.Clock.Now;
        if (!wasEngaged && _state.Emergency.IsEngaged)
        {
            var entered = _state.Emergency.Active;
            _state.EvidenceEmitter.EmergencyEntered(in entered, observedAt);
        }
        else if (wasEngaged && !_state.Emergency.IsEngaged)
        {
            _state.EvidenceEmitter.EmergencyCleared(in priorActive, observedAt);
        }

        if (engaged && !_state.IsInsideServiceCallback)
        {
            var rejected = RejectAllActiveBatches(
                observedAt,
                markFrameTransitions: _state.IsPumping);
            if (!_state.IsPumping)
                _state.Observability.RecordOutOfFrameEmergencyRejections(rejected);
        }
    }

    internal int RejectAllActiveBatches(
        MonotonicTimestamp now,
        bool markFrameTransitions)
    {
        var rejected = 0;
        var emergency = EffectiveEmergencyContext;
        if (!emergency.IsValid)
            throw new InvalidOperationException(
                "Emergency rejection requires an active or frame-latched context.");
        for (var ordinal = 0; ordinal < _state.Transitioned.Length; ordinal++)
        {
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var phase = slot.HandoffPhaseHint;
            if (phase is ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating or
                ServiceHandoffPhase.ResponseReady or ServiceHandoffPhase.MainOwnedBatch)
            {
                _state.EvidenceEmitter.EmergencyAppliedToService(ordinal, in emergency);
            }
            if (!slot.RejectForEmergencyStop(emergency, now, out var receipt)) continue;
            _state.EvidenceEmitter.EmergencyRejected(ordinal, in receipt, now);
            rejected++;
            slot.TryAdvancePendingMainOwnership(now);
            if (markFrameTransitions) _state.Transitioned[ordinal] = true;
        }
        return rejected;
    }
}
