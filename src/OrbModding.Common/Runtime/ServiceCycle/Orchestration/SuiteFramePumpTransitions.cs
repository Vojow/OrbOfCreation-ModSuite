using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpTransitions
{
    private readonly SuiteFramePumpState _state;
    private readonly SuiteFramePumpControl _control;

    internal SuiteFramePumpTransitions(
        SuiteFramePumpState state,
        SuiteFramePumpControl control)
    {
        _state = state;
        _control = control;
    }

    internal void AdvancePendingMainOwnership(
        int startOrdinal,
        MonotonicTimestamp observedAt)
    {
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed || !slot.TryAdvancePendingMainOwnership(observedAt)) continue;
            _state.Transitioned[ordinal] = true;
        }
    }

    internal void AcquireResponses(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed || slot.HandoffPhaseHint != ServiceHandoffPhase.ResponseReady)
                continue;
            var started = _state.Clock.Now;
            var acquisition = default(ServiceResponseAcquisition);
            var untracedTerminal = default(BatchReceipt);
            var acquired = _state.Traces.Dispatch is null && _state.Journal.Observer is null
                ? slot.TryAcquireResponseWithoutFacts(started, out untracedTerminal)
                : (acquisition = slot.TryAcquireResponse(started)).Acquired;
            var ended = _state.Clock.Now;
            if (acquired)
            {
                _state.EvidenceEmitter.ResponseAcquired(
                    ordinal,
                    in acquisition,
                    ended,
                    frameIdentity);
                metrics.Responses++;
                _state.Transitioned[ordinal] = true;
                if (acquisition.EmergencyRejected || untracedTerminal.HasEmergencyStopContext)
                    metrics.EmergencyRejections++;
                else if (_control.EffectiveEmergencyStop &&
                         slot.RejectForEmergencyStop(
                             _control.EffectiveEmergencyContext,
                             _state.Clock.Now,
                             out var receipt))
                {
                    _state.EvidenceEmitter.EmergencyRejected(
                        ordinal,
                        in receipt,
                        receipt.CompletedAt);
                    metrics.EmergencyRejections++;
                }
            }
            metrics.ResponseTicks = AddElapsed(metrics.ResponseTicks, started, ended);
        }
    }

    internal void DispatchActions(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        if (_control.EffectiveEmergencyStop) return;
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var started = _state.Clock.Now;
            ServiceActionDispatch dispatch;
#if SERVICE_CYCLE_PROFILE
            var profileCoordinates = new ServiceCycleProfileCoordinates(ordinal, frameIdentity);
#endif
            _state.EnterServiceCallback();
            try
            {
                dispatch = slot.TryExecuteOne(
                    started,
                    _state.Traces.Dispatch
#if SERVICE_CYCLE_PROFILE
                    , in profileCoordinates
#endif
                    );
            }
            finally { _state.ExitServiceCallback(); }
            var ended = _state.Clock.Now;
            _state.EvidenceEmitter.ActionDispatched(
                ordinal,
                in dispatch,
                ended,
                frameIdentity);
            if (dispatch.Attempted)
            {
                metrics.Actions++;
                _state.Transitioned[ordinal] = true;
                metrics.ActionTicks = AddElapsed(
                    metrics.ActionTicks,
                    dispatch.ActionFact.StartedAt,
                    dispatch.ActionFact.CompletedAt);
            }
            if (!_control.EffectiveEmergencyStop) continue;
            metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                ended,
                markFrameTransitions: true);
            break;
        }
    }

    internal void StartCycles(
        int startOrdinal,
        long frameIdentity,
        ref SuiteFramePumpFrameMetrics metrics)
    {
        if (_control.EffectiveEmergencyStop) return;
        for (var offset = 0; offset < _state.Transitioned.Length; offset++)
        {
            var ordinal = OrdinalAt(startOrdinal, offset);
            if (_state.Transitioned[ordinal]) continue;
            var slot = _state.Registry.GetSlot(ordinal);
            if (slot.IsDisposed) continue;
            var started = _state.Clock.Now;
            ServiceCycleStartAttempt start;
#if SERVICE_CYCLE_PROFILE
            var profileCoordinates = new ServiceCycleProfileCoordinates(ordinal, frameIdentity);
#endif
            _state.EnterServiceCallback();
            try
            {
                start = slot.TryStartCycle(
                    started,
                    _state.Traces.Dispatch
#if SERVICE_CYCLE_PROFILE
                    , in profileCoordinates
#endif
                    );
            }
            finally { _state.ExitServiceCallback(); }
            var ended = _state.Clock.Now;
            _state.EvidenceEmitter.StartAttemptObserved(
                ordinal,
                in start,
                _state.Registry.CurrentLifecycle,
                ended,
                frameIdentity);
            if (start.CaptureAttempted)
            {
                metrics.Captures++;
                _state.Transitioned[ordinal] = true;
                metrics.CaptureTicks = AddElapsed(
                    metrics.CaptureTicks,
                    start.CaptureFact.StartedAt,
                    start.CaptureFact.CompletedAt);
            }
            if (!_control.EffectiveEmergencyStop) continue;
            metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                ended,
                markFrameTransitions: true);
            break;
        }
    }

    private int OrdinalAt(int startOrdinal, int offset) =>
        _state.Transitioned.Length == 0
            ? 0
            : (startOrdinal + offset) % _state.Transitioned.Length;

    private static long AddElapsed(
        long total,
        MonotonicTimestamp start,
        MonotonicTimestamp end)
    {
        var elapsed = end.Ticks >= start.Ticks ? end.Ticks - start.Ticks : 0;
        return elapsed > long.MaxValue - total ? long.MaxValue : total + elapsed;
    }
}
