using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private ServiceCycleStartAttempt TryCapture(
        ConfigurationPublication<TConfig> configuration,
        in ServiceStartDecisionFact startFact,
        in ServiceStartInvocationFact startInvocation,
        bool nonBlockingProbe,
        int ordinal,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        )
    {
        var capture = new CaptureSequence(checked(++_captureSequence));
        var cycle = new CycleId(checked(++_cycleSequence));
        var batch = new BatchId(checked(++_batchSequence));
        var captureAttemptedAt = _clock.Now;
        var captureContext = new ServiceCaptureContext(
            _serviceId,
            _lifecycle,
            configuration.Generation,
            capture,
            cycle,
            captureAttemptedAt
#if SERVICE_CYCLE_PROFILE
            , in profileCoordinates
#endif
            );
        observer?.CaptureStarted(ordinal, in captureContext);
        var captureStartedAt = _clock.Now;
        ServiceCaptureResult result;
        var reusableFrame = _frame.Value;
        IsCapturing = true;
        try
        {
            var snapshot = configuration.Snapshot;
            result = _definition.Capture(
                ref _frame.Value,
                in snapshot,
                in captureContext);
            if (!typeof(TFrame).IsValueType &&
                !ReferenceEquals(reusableFrame, _frame.Value))
            {
                _frame.Value = reusableFrame;
                throw new InvalidOperationException(
                    "Capture cannot replace a reference-type reusable frame instance.");
            }
            if (!result.IsValid)
                throw new InvalidOperationException(
                    "The service returned an invalid capture result.");
        }
        catch (Exception exception) when (
            ServiceCycleFatalExceptionPolicy.MustEscape(_definition, exception))
        {
            if (!typeof(TFrame).IsValueType &&
                !ReferenceEquals(reusableFrame, _frame.Value))
                _frame.Value = reusableFrame;
            throw;
        }
        catch
        {
            if (!typeof(TFrame).IsValueType &&
                !ReferenceEquals(reusableFrame, _frame.Value))
                _frame.Value = reusableFrame;
            var captureCompletedAt = _clock.Now;
            var record = RecordCaptureFault(captureCompletedAt);
            var captureFact = new ServiceCaptureFact(
                captureContext,
                default,
                captureStartedAt,
                captureCompletedAt,
                record.Fault,
                record.RetryDue);
            return new ServiceCycleStartAttempt(
                false, startFact, captureFact, default, batch, default,
                record.Fault, record.RetryDue,
                startInvocation: startInvocation);
        }
        finally
        {
            IsCapturing = false;
        }
        var captureObservedAt = _clock.Now;
        var committedCapture = new ServiceCaptureFact(
            captureContext,
            result,
            captureStartedAt,
            captureObservedAt);
        _state.LastCapture = committedCapture;
        var recoveredCaptureFault = _captureFaults.Recover(captureObservedAt);
        ClearRecoveredCaptureFault(in recoveredCaptureFault);

        if (_lifetime.IsSuperseded)
        {
            var supersededCycle = result.IsCaptured
                ? new ServiceCycleIdentity(
                    _serviceId,
                    _lifecycle,
                    configuration.Generation,
                    result.StrategyGeneration,
                    capture,
                    cycle)
                : default;
            return new ServiceCycleStartAttempt(
                false, startFact, committedCapture, supersededCycle, batch,
                default,
                recoveredFault: recoveredCaptureFault,
                startInvocation: startInvocation);
        }

        if (!result.IsCaptured)
        {
            _state.NextWakeDue = ServiceWakeSchedule.FromRetryPolicy(
                result.WakePolicy,
                captureObservedAt);
            _state.HasWakeDue = true;
            return new ServiceCycleStartAttempt(
                false, startFact, committedCapture, default, batch, default,
                recoveredFault: recoveredCaptureFault,
                startInvocation: startInvocation);
        }

        var identity = new ServiceCycleIdentity(
            _serviceId,
            _lifecycle,
            configuration.Generation,
            result.StrategyGeneration,
            capture,
            cycle);
        var context = new ServiceCycleContext(
            identity,
            _state.PreviousReceipt,
            captureObservedAt);
        _state.CycleConfiguration = configuration;
        if (_lifetime.IsSuperseded)
        {
            _state.CycleConfiguration = null;
            return new ServiceCycleStartAttempt(
                false, startFact, committedCapture, identity, batch, default,
                recoveredFault: recoveredCaptureFault,
                startInvocation: startInvocation);
        }
        var queuedAt = _clock.Now;
        var published = nonBlockingProbe
            ? _handoff.TryPublishRequestNonBlocking(
                configuration,
                in context,
                batch,
                out _)
            : _handoff.TryPublishRequest(
                configuration,
                in context,
                batch,
                out _);
        if (!published)
        {
            if (nonBlockingProbe)
            {
                _hasPendingRequest = true;
                _pendingConfiguration = configuration;
                _pendingContext = context;
                _pendingBatch = batch;
                _pendingStart = startFact;
            }
            else
            {
                _state.CycleConfiguration = null;
            }
            return new ServiceCycleStartAttempt(
                false, startFact, committedCapture, identity, batch, default,
                recoveredFault: recoveredCaptureFault,
                startInvocation: startInvocation);
        }

        _state.HasWakeDue = false;
        _state.InFlightCycle = identity;
        _state.InFlightBatch = batch;
        _state.HasInFlightCycle = true;
        return new ServiceCycleStartAttempt(
            true, startFact, committedCapture, identity, batch, queuedAt,
            recoveredFault: recoveredCaptureFault,
            startInvocation: startInvocation);
    }
}
