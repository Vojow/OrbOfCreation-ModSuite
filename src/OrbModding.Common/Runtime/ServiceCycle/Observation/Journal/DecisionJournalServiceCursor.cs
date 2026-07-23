using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal partial struct DecisionJournalServiceCursor
{
    private DecisionJournalPendingDecision _pending;
    private bool _hasPending;
    private ServiceFault _constructionFault;
    private ServiceFault _faultState;

    internal ServiceCycleTraceServiceId Service { get; private set; }
    internal LifecycleGeneration ActiveLifecycle { get; private set; }
    internal ConfigGeneration Configuration { get; private set; }
    internal StrategyGeneration Strategy { get; private set; }
    internal LifecycleGeneration RequestedLifecycle { get; private set; }
    internal long LifecycleSemanticVersion { get; set; }
    internal long LifecycleTerminalSequence { get; set; }
    internal long ConstructionDeferralSequence { get; set; }
    internal bool IsBound => Service.IsValid;
    internal bool HasPending => _hasPending;
    internal bool HasUnqueuedPending => _hasPending && !_pending.Queued;

    internal void Bind(
        ServiceCycleTraceServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence)
    {
        if (IsBound) throw new InvalidOperationException("The journal service is already bound.");
        if (!service.IsValid) throw new ArgumentException("A valid journal service is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle is required.", nameof(lifecycle));
        if (!configuration.IsValid)
            throw new ArgumentException("A valid configuration is required.", nameof(configuration));
        if (lifecycleSemanticVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycleSemanticVersion));
        if (lifecycleTerminalSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycleTerminalSequence));
        if (constructionDeferralSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(constructionDeferralSequence));
        Service = service;
        ActiveLifecycle = lifecycle;
        RequestedLifecycle = lifecycle;
        Configuration = configuration;
        Strategy = strategy;
        _faultState = fault;
        LifecycleSemanticVersion = lifecycleSemanticVersion;
        LifecycleTerminalSequence = lifecycleTerminalSequence;
        ConstructionDeferralSequence = constructionDeferralSequence;
    }

    internal void SetConfiguration(ConfigGeneration generation) => Configuration = generation;
    internal void SetStrategy(StrategyGeneration generation) => Strategy = generation;
    internal void RequestLifecycle(LifecycleGeneration lifecycle) => RequestedLifecycle = lifecycle;
    internal void ActivateLifecycle(LifecycleGeneration lifecycle)
    {
        if (_hasPending)
            throw new InvalidOperationException("The retired journal cycle must close before lifecycle activation.");
        ActiveLifecycle = lifecycle;
        _constructionFault = default;
        _faultState = default;
    }

    internal void ObserveFaultTransition(
        ServiceFaultRecoveryFact recovery,
        ServiceFault fault)
    {
        if (recovery.IsPresent)
        {
            var recovered = recovery.Fault;
            if (SameFault(in recovered, in _faultState)) _faultState = default;
        }
        if (fault.IsValid) _faultState = fault;
        if (_hasPending) _pending.Fault = _faultState;
    }

    private static bool SameFault(in ServiceFault left, in ServiceFault right) =>
        left.IsValid == right.IsValid &&
        (!left.IsValid || left.Category == right.Category && left.Code == right.Code &&
            left.OccurrenceCount == right.OccurrenceCount && left.ObservedAt == right.ObservedAt);
}
