using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

/// <summary>
/// Keeps the always-on live projection and the independently-lived disk journal on the exact same
/// evidence stream. The secondary consumer can arm and stop without resetting the primary.
/// </summary>
internal sealed class ServiceCycleDecisionJournalObserverFanout :
    IServiceCycleDecisionJournalObserver
{
    private readonly IServiceCycleDecisionJournalObserver _primary;
    private IServiceCycleDecisionJournalObserver? _secondary;

    internal ServiceCycleDecisionJournalObserverFanout(
        IServiceCycleDecisionJournalObserver primary) =>
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));

    public bool IsFaulted => _primary.IsFaulted || _secondary?.IsFaulted == true;
    public Exception? FaultException => _primary.FaultException ?? _secondary?.FaultException;
    public string? FaultSite => _primary.FaultSite ?? _secondary?.FaultSite;

    internal void Attach(IServiceCycleDecisionJournalObserver secondary)
    {
        if (secondary is null) throw new ArgumentNullException(nameof(secondary));
        if (_secondary is not null)
            throw new InvalidOperationException("A secondary decision-journal observer is already attached.");
        _secondary = secondary;
    }

    internal void Detach(IServiceCycleDecisionJournalObserver secondary)
    {
        if (!ReferenceEquals(_secondary, secondary))
            throw new InvalidOperationException("The requested decision-journal observer is not attached.");
        _secondary = null;
    }

    public void BindPublications(ConfigGeneration configuration, StrategyGeneration strategy)
    {
        _primary.BindPublications(configuration, strategy);
        _secondary?.BindPublications(configuration, strategy);
    }

    public void Bind(
        int ordinal,
        LifecycleGeneration lifecycle,
        ServiceFault fault,
        long lifecycleSemanticVersion,
        long lifecycleTerminalSequence,
        long constructionDeferralSequence,
        long worldGateDeferralSequence)
    {
        _primary.Bind(
            ordinal,
            lifecycle,
            fault,
            lifecycleSemanticVersion,
            lifecycleTerminalSequence,
            constructionDeferralSequence,
            worldGateDeferralSequence);
        _secondary?.Bind(
            ordinal,
            lifecycle,
            fault,
            lifecycleSemanticVersion,
            lifecycleTerminalSequence,
            constructionDeferralSequence,
            worldGateDeferralSequence);
    }

    public void ObservePublications(
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        _primary.ObservePublications(configuration, strategy, observedAt);
        _secondary?.ObservePublications(configuration, strategy, observedAt);
    }

    public void LifecycleRequested(
        int ordinal,
        LifecycleGeneration lifecycle,
        MonotonicTimestamp observedAt)
    {
        _primary.LifecycleRequested(ordinal, lifecycle, observedAt);
        _secondary?.LifecycleRequested(ordinal, lifecycle, observedAt);
    }

    public bool NeedsLifecycleObservation(int ordinal, long lifecycleSemanticVersion)
    {
        var primaryNeedsObservation =
            _primary.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion);
        var secondaryNeedsObservation =
            _secondary?.NeedsLifecycleObservation(ordinal, lifecycleSemanticVersion) == true;
        return primaryNeedsObservation || secondaryNeedsObservation;
    }

    public void ObserveLifecycle(
        int ordinal,
        in ServiceLifecycleSlotSnapshot snapshot,
        long lifecycleSemanticVersion,
        MonotonicTimestamp observedAt)
    {
        _primary.ObserveLifecycle(
            ordinal,
            in snapshot,
            lifecycleSemanticVersion,
            observedAt);
        _secondary?.ObserveLifecycle(
            ordinal,
            in snapshot,
            lifecycleSemanticVersion,
            observedAt);
    }

    public void ObserveWorldGate(
        int ordinal,
        in ServiceWorldGateDeferralFact deferral,
        MonotonicTimestamp observedAt)
    {
        _primary.ObserveWorldGate(ordinal, in deferral, observedAt);
        _secondary?.ObserveWorldGate(ordinal, in deferral, observedAt);
    }

    public void EmergencyEntered(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        _primary.EmergencyEntered(in emergency, observedAt);
        _secondary?.EmergencyEntered(in emergency, observedAt);
    }

    public void EmergencyCleared(
        in EmergencyStopContext emergency,
        MonotonicTimestamp observedAt)
    {
        _primary.EmergencyCleared(in emergency, observedAt);
        _secondary?.EmergencyCleared(in emergency, observedAt);
    }

    public void StartAttemptObserved(
        int ordinal,
        in ServiceCycleStartAttempt attempt,
        MonotonicTimestamp observedAt)
    {
        _primary.StartAttemptObserved(ordinal, in attempt, observedAt);
        _secondary?.StartAttemptObserved(ordinal, in attempt, observedAt);
    }

    public void ResponseAcquired(
        int ordinal,
        in ServiceResponseAcquisition acquisition,
        MonotonicTimestamp observedAt)
    {
        _primary.ResponseAcquired(ordinal, in acquisition, observedAt);
        _secondary?.ResponseAcquired(ordinal, in acquisition, observedAt);
    }

    public void ActionDispatched(
        int ordinal,
        in ServiceActionDispatch dispatch,
        MonotonicTimestamp observedAt)
    {
        _primary.ActionDispatched(ordinal, in dispatch, observedAt);
        _secondary?.ActionDispatched(ordinal, in dispatch, observedAt);
    }

    public void EmergencyRejected(
        int ordinal,
        in BatchReceipt receipt,
        MonotonicTimestamp observedAt)
    {
        _primary.EmergencyRejected(ordinal, in receipt, observedAt);
        _secondary?.EmergencyRejected(ordinal, in receipt, observedAt);
    }

    public void Advance(MonotonicTimestamp observedAt)
    {
        _primary.Advance(observedAt);
        _secondary?.Advance(observedAt);
    }

    public void Stop(MonotonicTimestamp observedAt)
    {
        _primary.Stop(observedAt);
        _secondary?.Stop(observedAt);
    }
}
