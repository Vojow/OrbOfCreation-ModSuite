using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpJournalSession
{
    private readonly ServiceCycleRegistry _registry;
    private readonly int _serviceCapacity;
    private IServiceCycleDecisionJournalObserver? _observer;
    private object? _runtimeOwner;

    internal SuiteFramePumpJournalSession(
        ServiceCycleRegistry registry,
        int serviceCapacity)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceCapacity = serviceCapacity;
    }

    internal IServiceCycleDecisionJournalObserver? Observer => _observer;

    internal bool TryAttach(
        IServiceCycleDecisionJournalObserver observer,
        DecisionJournalServiceBaseline[] baselines,
        bool emergencyEngaged)
    {
        if (_observer is not null)
            throw new InvalidOperationException("A decision journal is already attached.");
        if (emergencyEngaged) return false;
        if (!ServiceCycleDecisionJournalBinder.TryBind(
                _registry,
                _serviceCapacity,
                observer,
                baselines)) return false;
        _observer = observer;
        return true;
    }

    internal void Detach(IServiceCycleDecisionJournalObserver observer)
    {
        if (!ReferenceEquals(_observer, observer))
            throw new InvalidOperationException("The requested decision journal is not attached.");
        _observer = null;
    }

    internal void ClaimRuntime(object owner)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        if (_runtimeOwner is not null)
            throw new InvalidOperationException("A decision-journal runtime already owns this pump.");
        _runtimeOwner = owner;
    }

    internal void ReleaseRuntime(object owner)
    {
        EnsureOwner(owner);
        if (_observer is not null)
            throw new InvalidOperationException("An attached decision journal cannot release runtime ownership.");
        _runtimeOwner = null;
    }

    internal void ValidateOwnedPumpDisposal(object owner)
    {
        EnsureOwner(owner);
        if (_observer is not null)
            throw new InvalidOperationException("An attached decision journal cannot dispose the frame pump.");
    }

    internal void CompleteOwnedPumpDisposal(object owner)
    {
        EnsureOwner(owner);
        _runtimeOwner = null;
    }

    internal void EnsureUnowned()
    {
        if (_runtimeOwner is not null)
            throw new InvalidOperationException(
                "The decision-journal runtime must reach terminal state before the frame pump is disposed.");
    }

    private void EnsureOwner(object owner)
    {
        if (!ReferenceEquals(_runtimeOwner, owner))
            throw new InvalidOperationException("The requested runtime does not own this pump.");
    }
}
