using System;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpExecutor
{
    private readonly SuiteFramePumpState _state;
    private readonly SuiteFramePumpControl _control;
    private readonly SuiteFramePumpTransitions _transitions;

    internal SuiteFramePumpExecutor(
        SuiteFramePumpState state,
        SuiteFramePumpControl control)
    {
        _state = state;
        _control = control;
        _transitions = new SuiteFramePumpTransitions(state, control);
    }

    internal SuiteFramePumpReport Pump(long frameIdentity)
    {
        _state.EnsureAvailable();
        if (frameIdentity < 0) throw new ArgumentOutOfRangeException(nameof(frameIdentity));
        if (_state.IsPumping)
            throw new InvalidOperationException(
                "The service-cycle frame pump cannot be entered recursively.");
        if (_state.Observability.HasAcceptedFrame &&
            frameIdentity <= _state.Observability.LastAcceptedFrameIdentity)
            return RejectFrame(frameIdentity);

        _state.BeginPump();
#if SERVICE_CYCLE_PROFILE
        var pumpProfile = _state.EvidenceProfiler.BeginPump(
            _state.Registry.CurrentLifecycle.Value,
            frameIdentity);
        var pumpCompleted = false;
#endif
        _state.PrepareFrame();
        try
        {
            var report = PumpAcceptedFrame(frameIdentity);
#if SERVICE_CYCLE_PROFILE
            pumpCompleted = true;
#endif
            return report;
        }
        finally
        {
            _state.EndFrame();
#if SERVICE_CYCLE_PROFILE
            if (pumpCompleted) pumpProfile.Complete();
            else pumpProfile.Abandon();
#endif
        }
    }

    private SuiteFramePumpReport RejectFrame(long frameIdentity)
    {
        var report = new SuiteFramePumpReport(
            frameIdentity,
            false,
            _state.NextStartOrdinal,
            0,
            0,
            0,
            0,
            0,
            default,
            default,
            default,
            default);
        _state.Observability.RecordReport(in report);
        _state.EvidenceEmitter.RejectedPumpCompleted(in report, _state.Clock.Now);
        return report;
    }

    private SuiteFramePumpReport PumpAcceptedFrame(long frameIdentity)
    {
        var frameStarted = _state.Clock.Now;
        var startOrdinal = _state.NextStartOrdinal;
        var lifecycleTransitionStart =
            _state.Registry.LifecyclePositionTransitionCount;
        var reconciliationEpoch =
            _state.Registry.NextLifecycleReconciliationEpoch();
        var metrics = default(SuiteFramePumpFrameMetrics);

        _state.EvidenceScanner.ObservePublications(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            frameStarted,
            includeJournal: true);
        ReconcileLifecycle(frameStarted, reconciliationEpoch);
        _transitions.AdvancePendingMainOwnership(startOrdinal, frameStarted);
        _transitions.AcquireResponses(startOrdinal, frameIdentity, ref metrics);
        if (_control.EffectiveEmergencyStop)
        {
            metrics.EmergencyRejections += _control.RejectAllActiveBatches(
                _state.Clock.Now,
                markFrameTransitions: true);
        }
        _transitions.DispatchActions(startOrdinal, frameIdentity, ref metrics);
        _transitions.StartCycles(startOrdinal, frameIdentity, ref metrics);
        _state.Registry.ReconcileLifecycle(_state.Clock.Now, reconciliationEpoch);
        _state.EvidenceScanner.ObserveLifecycle(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            _state.Clock.Now);

        if (_state.Transitioned.Length != 0)
            _state.NextStartOrdinal =
                (startOrdinal + 1) % _state.Transitioned.Length;
        var frameEnded = _state.Clock.Now;
        var report = CreateReport(
            frameIdentity,
            startOrdinal,
            lifecycleTransitionStart,
            frameStarted,
            frameEnded,
            metrics);
        _state.Observability.RecordReport(in report);
        _state.EvidenceScanner.ObservePublications(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            frameEnded,
            includeJournal: false);
        _state.EvidenceEmitter.PumpCompleted(
            in report,
            _state.Registry.CurrentLifecycle,
            frameEnded);
        return report;
    }

    private void ReconcileLifecycle(
        MonotonicTimestamp observedAt,
        long reconciliationEpoch)
    {
        _state.Registry.ReconcileLifecycle(observedAt, reconciliationEpoch);
        _state.EvidenceScanner.ObserveLifecycle(
            _state.Traces.Dispatch,
            _state.Journal.Observer,
            observedAt);
    }

    private SuiteFramePumpReport CreateReport(
        long frameIdentity,
        int startOrdinal,
        long lifecycleTransitionStart,
        MonotonicTimestamp frameStarted,
        MonotonicTimestamp frameEnded,
        SuiteFramePumpFrameMetrics metrics) =>
        new(
            frameIdentity,
            true,
            startOrdinal,
            metrics.Responses,
            metrics.Actions,
            metrics.Captures,
            metrics.EmergencyRejections,
            _state.Registry.LifecyclePositionTransitionCount - lifecycleTransitionStart,
            new MonotonicDuration(metrics.ResponseTicks),
            new MonotonicDuration(metrics.ActionTicks),
            new MonotonicDuration(metrics.CaptureTicks),
            new MonotonicDuration(ElapsedTicks(frameStarted, frameEnded)));

    private static long ElapsedTicks(
        MonotonicTimestamp start,
        MonotonicTimestamp end) =>
        end.Ticks >= start.Ticks ? end.Ticks - start.Ticks : 0;
}
