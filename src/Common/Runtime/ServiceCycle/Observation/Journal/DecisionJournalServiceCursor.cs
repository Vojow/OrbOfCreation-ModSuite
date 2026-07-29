using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal partial struct DecisionJournalServiceCursor
{
    internal const int WorldBehindLastActionCode = 1;
    internal const int WorldUnansweredCode = 2;

    private DecisionJournalPendingDecision _pending;
    private bool _hasPending;
    private ServiceFault _constructionFault;
    private ServiceFault _faultState;
    private long _heldActionFrame;
    private int _heldWorldGateCode;

    internal ServiceCycleTraceServiceId Service { get; private set; }
    internal LifecycleGeneration ActiveLifecycle { get; private set; }
    internal LifecycleGeneration RequestedLifecycle { get; private set; }
    internal long LifecycleSemanticVersion { get; set; }
    internal long LifecycleTerminalSequence { get; set; }
    internal long ConstructionDeferralSequence { get; set; }
    internal long WorldGateDeferralSequence { get; set; }
    internal bool IsBound => Service.IsValid;
    internal bool HasPending => _hasPending;
    internal bool HasUnqueuedPending => _hasPending && !_pending.Queued;

    internal void Bind(
        ServiceCycleTraceServiceId service,
        LifecycleGeneration lifecycle,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence,
        long worldGateDeferralSequence)
    {
        if (IsBound) throw new InvalidOperationException("The journal service is already bound.");
        if (!service.IsValid) throw new ArgumentException("A valid journal service is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle is required.", nameof(lifecycle));
        if (lifecycleSemanticVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycleSemanticVersion));
        if (lifecycleTerminalSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(lifecycleTerminalSequence));
        if (constructionDeferralSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(constructionDeferralSequence));
        if (worldGateDeferralSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(worldGateDeferralSequence));
        Service = service;
        ActiveLifecycle = lifecycle;
        RequestedLifecycle = lifecycle;
        _faultState = fault;
        LifecycleSemanticVersion = lifecycleSemanticVersion;
        LifecycleTerminalSequence = lifecycleTerminalSequence;
        ConstructionDeferralSequence = constructionDeferralSequence;
        WorldGateDeferralSequence = worldGateDeferralSequence;
    }

    /// <summary>
    /// Whether a fresh world-gate deferral opens a hold rather than continuing one already recorded.
    /// </summary>
    /// <remarks>
    /// The gate re-defers a held service on every frame, so a hold is identified by the action it is
    /// waiting past and by why the world is not good enough yet. A service stuck behind a stalled
    /// collector therefore leaves one record whose missing end is the stall, rather than a record per
    /// frame for as long as it stays stuck.
    /// </remarks>
    internal bool TryOpenWorldGateHold(in ServiceWorldGateDeferralFact deferral, out int code)
    {
        code = deferral.World.IsValid ? WorldBehindLastActionCode : WorldUnansweredCode;
        if (!deferral.IsPresent || deferral.Sequence <= WorldGateDeferralSequence) return false;
        WorldGateDeferralSequence = deferral.Sequence;
        if (_heldActionFrame == deferral.LastActionFrame && _heldWorldGateCode == code) return false;
        _heldActionFrame = deferral.LastActionFrame;
        _heldWorldGateCode = code;
        return true;
    }

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
