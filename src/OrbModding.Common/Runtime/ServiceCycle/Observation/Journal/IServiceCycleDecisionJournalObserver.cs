using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal interface IServiceCycleDecisionJournalObserver
{
    bool IsFaulted { get; }

    void Bind(
        int ordinal,
        LifecycleGeneration lifecycle,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence);

    void ObservePublications(
        int ordinal,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt);

    void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt);

    bool NeedsLifecycleObservation(int ordinal, long lifecycleSemanticVersion);

    void ObserveLifecycle(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt);

    void EmergencyEntered(in EmergencyStopContext emergency, MonotonicTimestamp observedAt);
    void EmergencyCleared(in EmergencyStopContext emergency, MonotonicTimestamp observedAt);

    void StartAttemptObserved(
        int ordinal,
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt);

    void ResponseAcquired(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt);

    void ActionDispatched(
        int ordinal,
        in ServiceActionDispatch dispatch,
        MonotonicTimestamp observedAt);

    void EmergencyRejected(
        int ordinal,
        in BatchReceipt receipt,
        MonotonicTimestamp observedAt);

    void Advance(MonotonicTimestamp observedAt);
    void Stop(MonotonicTimestamp observedAt);
}
