using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    public ServiceHandoffPhase HandoffPhaseHint =>
        CurrentRunner?.HandoffPhaseHint ?? ServiceHandoffPhase.Stopped;

    public bool IsBetweenCycles =>
        _position0.IsBetweenCycles && _position1.IsBetweenCycles;

    public bool TryGetRunnerSnapshot(out ServiceRunnerSnapshot snapshot)
    {
        var runner = CurrentRunner;
        if (runner is null)
        {
            snapshot = default;
            return false;
        }
        return runner.TrySnapshot(out snapshot);
    }

    public ServiceResponseAcquisition TryAcquireResponse(
        MonotonicTimestamp now) =>
        CurrentRunner?.TryAcquireResponseNonBlocking(now) ?? default;

    public bool TryAcquireResponseWithoutFacts(
        MonotonicTimestamp now,
        out BatchReceipt terminalReceipt)
    {
        var runner = CurrentRunner;
        if (runner is null)
        {
            terminalReceipt = default;
            return false;
        }
        return runner.TryAcquireResponseNonBlockingWithoutFacts(
            now,
            out terminalReceipt);
    }

    public bool TryAdvancePendingMainOwnership(MonotonicTimestamp now) =>
        CurrentRunner?.TryAdvancePendingMainOwnership() ?? false;

    public ServiceActionDispatch TryExecuteOne(
        MonotonicTimestamp now,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        )
    {
        var runner = CurrentRunner;
        if (runner is null) return default;
        return
#if SERVICE_CYCLE_PROFILE
            runner.TryExecuteOneNonBlockingProfiled(
#else
            runner.TryExecuteOneNonBlocking(
#endif
            now,
            Ordinal,
            observer
#if SERVICE_CYCLE_PROFILE
            , in profileCoordinates
#endif
            );
    }

    public ServiceCycleStartAttempt TryStartCycle(
        MonotonicTimestamp now,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        )
    {
        var runner = CurrentRunner;
        if (runner is null) return default;
        return runner.TryStartCycleNonBlocking(
            now
#if SERVICE_CYCLE_PROFILE
            , in profileCoordinates
#endif
            ,
            Ordinal,
            observer);
    }

    public bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt)
    {
        var runner = CurrentRunner;
        if (runner is null)
        {
            receipt = default;
            return false;
        }
        return runner.RejectForEmergencyStop(emergency, now, out receipt);
    }
}
