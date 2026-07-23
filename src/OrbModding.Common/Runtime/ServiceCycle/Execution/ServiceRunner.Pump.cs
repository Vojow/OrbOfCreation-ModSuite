using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

public sealed partial class ServiceRunner<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceCycleStartAttempt TryStartCycle(
        MonotonicTimestamp now
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        )
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _starts.TryStart(
                now
#if SERVICE_CYCLE_PROFILE
                , in profileCoordinates
#endif
                );
    }

    internal ServiceCycleStartAttempt TryStartCycleNonBlocking(
        MonotonicTimestamp now
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        ,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _starts.TryStart(
                now
#if SERVICE_CYCLE_PROFILE
                , in profileCoordinates
#endif
                ,
                nonBlockingProbe: true,
                ordinal,
                observer);
    }

    internal bool TryAcquireResponse()
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _responses.TryAcquire();
    }

    internal ServiceResponseAcquisition TryAcquireResponseNonBlocking(
        MonotonicTimestamp now)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _responses.TryAcquireNonBlocking(now);
    }

    internal bool TryAcquireResponseNonBlockingWithoutFacts(
        MonotonicTimestamp now,
        out BatchReceipt terminalReceipt)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        if (_lifetime.IsSuperseded)
        {
            terminalReceipt = default;
            return false;
        }
        return _responses.TryAcquireNonBlockingWithoutFacts(
            now,
            out terminalReceipt);
    }

    internal ServiceActionDispatch TryExecuteOne(MonotonicTimestamp now)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _actionExecutor.TryExecuteOne(now);
    }

    internal ServiceActionDispatch TryExecuteOneNonBlocking(
        MonotonicTimestamp now,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
#if SERVICE_CYCLE_PROFILE
        var profileCoordinates = default(ServiceCycleProfileCoordinates);
        return TryExecuteOneNonBlockingProfiled(
            now,
            ordinal,
            observer,
            in profileCoordinates);
#else
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _actionExecutor.TryExecuteOneNonBlocking(now, ordinal, observer);
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionDispatch TryExecuteOneNonBlockingProfiled(
        MonotonicTimestamp now,
        int ordinal,
        IServiceCycleAttemptObserver? observer,
        in ServiceCycleProfileCoordinates profileCoordinates)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _lifetime.IsSuperseded
            ? default
            : _actionExecutor.TryExecuteOneNonBlockingProfiled(
                now,
                ordinal,
                observer,
                in profileCoordinates);
    }
#endif

    internal bool TryAdvancePendingMainOwnership()
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        return _batchCompletion.TryAdvancePendingMainOwnership();
    }

    internal bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        AssertOwnerThread();
        _handoff.ThrowIfWorkerFatal();
        ThrowIfDisposed();
        if (!emergency.IsValid)
            throw new System.ArgumentException(
                "A valid emergency context is required.",
                nameof(emergency));
        _starts.CancelPendingRequestForEmergency();
        var phase = _handoff.PhaseHint;
        if (phase is ServiceHandoffPhase.RequestReady or
            ServiceHandoffPhase.Evaluating or
            ServiceHandoffPhase.ResponseReady)
        {
            _responses.MarkOutstandingEmergency(emergency);
        }
        return _batchCompletion.RejectForEmergencyStop(
            emergency,
            now,
            nonBlockingHandoff: true,
            out receipt);
    }
}
