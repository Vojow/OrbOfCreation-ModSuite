using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal interface IServiceCycleSlot : IDisposable
{
    long RegistrationToken { get; }
    int Ordinal { get; }
    ServiceId ServiceId { get; }
    bool IsDisposed { get; }
    long LifecyclePositionTransitionCount { get; }
    long LifecycleSemanticVersion { get; }
    ServiceHandoffPhase HandoffPhaseHint { get; }
    bool IsBetweenCycles { get; }
    ServiceLifecycleSlotSnapshot LifecycleSnapshot { get; }
    ConfigGeneration LatestConfiguration { get; }
    StrategyGeneration LatestStrategy { get; }
    bool TryGetRunnerSnapshot(out ServiceRunnerSnapshot snapshot);
    ServiceResponseAcquisition TryAcquireResponse(MonotonicTimestamp now);
    bool TryAcquireResponseWithoutFacts(MonotonicTimestamp now, out BatchReceipt terminalReceipt);
    bool TryAdvancePendingMainOwnership(MonotonicTimestamp now);
    ServiceActionDispatch TryExecuteOne(
        MonotonicTimestamp now,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        );
    ServiceCycleStartAttempt TryStartCycle(
        MonotonicTimestamp now,
        IServiceCycleAttemptObserver? observer
#if SERVICE_CYCLE_PROFILE
        , in ServiceCycleProfileCoordinates profileCoordinates
#endif
        );
    bool RejectForEmergencyStop(
        EmergencyStopContext emergency,
        MonotonicTimestamp now,
        out BatchReceipt receipt);
    void RequestLifecycle(LifecycleGeneration generation);
    void BindStrategy(IServiceStrategyGenerationSource strategy);
    bool ReconcileLifecycle(MonotonicTimestamp now, long reconciliationEpoch);
}
