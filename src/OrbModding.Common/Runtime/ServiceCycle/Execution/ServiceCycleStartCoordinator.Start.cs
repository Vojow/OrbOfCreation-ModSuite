using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceCycleStartAttempt TryStart(
        MonotonicTimestamp now
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        ,
        bool nonBlockingProbe = false,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
        if (_lifetime.IsSuperseded) return default;
        if (_hasPendingRequest)
            return TryPublishPendingRequest(nonBlockingProbe);
        if (_state.HasWakeDue && now < _state.NextWakeDue)
            return default;
        ServiceHandoffSnapshot handoff;
        if (nonBlockingProbe)
        {
            if (_handoff.PhaseHint != ServiceHandoffPhase.Empty ||
                !_handoff.TrySnapshot(out handoff))
                return default;
        }
        else
        {
            handoff = _handoff.Snapshot;
        }
        if (handoff.Phase != ServiceHandoffPhase.Empty || handoff.CleanupPending)
            return default;

        var configuration = _configuration.ReadLatest();
        _state.LatestConfigGeneration = configuration.Generation;
        var startContext = new ServiceCycleStartContext(
            _lifecycle,
            configuration.Generation,
            _state.PreviousReceipt,
            now);
        var startAttemptedAt = _clock.Now;
        observer?.StartAttempted(ordinal, in startContext, startAttemptedAt);
        ServiceStartDecision start;
        IsInvokingStartCallback = true;
        try
        {
            var snapshot = configuration.Snapshot;
            start = _definition.ShouldStart(in snapshot, in startContext);
            if (!start.IsValid)
                throw new InvalidOperationException(
                    "The service returned an invalid start decision.");
        }
        catch (Exception exception) when (
            !ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception))
        {
            var startFaultedAt = _clock.Now;
            var record = RecordCaptureFault(startFaultedAt);
            var invocation = new ServiceStartInvocationFact(
                startContext,
                startAttemptedAt,
                startFaultedAt);
            return new ServiceCycleStartAttempt(
                false, default, default, default, default, default,
                record.Fault, record.RetryDue, startInvocation: invocation);
        }
        finally
        {
            IsInvokingStartCallback = false;
        }
        var startObservedAt = _clock.Now;
        var startInvocation = new ServiceStartInvocationFact(
            startContext,
            startAttemptedAt,
            startObservedAt);
        var startFact = new ServiceStartDecisionFact(start, startObservedAt);
        _state.LastStartDecision = startFact;

        if (start.ShouldStart)
            observer?.StartReady(
                ordinal,
                in startContext,
                in start,
                startObservedAt,
                new MonotonicDuration(
                    startObservedAt.Ticks - startAttemptedAt.Ticks));

        if (_lifetime.IsSuperseded)
            return new ServiceCycleStartAttempt(
                false, startFact, default, default, default, default,
                startInvocation: startInvocation);

        if (!start.ShouldStart)
        {
            _state.NextWakeDue = ServiceWakeSchedule.FromRetryPolicy(
                start.WakePolicy,
                startObservedAt);
            _state.HasWakeDue = true;
            var recoveredFault = _captureFaults.Recover(startObservedAt);
            ClearRecoveredCaptureFault(in recoveredFault);
            return new ServiceCycleStartAttempt(
                false, startFact, default, default, default, default,
                recoveredFault: recoveredFault,
                startInvocation: startInvocation);
        }

        return TryCapture(
            configuration,
            in startFact,
            in startInvocation,
            nonBlockingProbe,
            ordinal,
            observer
#if SERVICE_CYCLE_PROFILE
            , in profileCoordinates
#endif
            );
    }
}
