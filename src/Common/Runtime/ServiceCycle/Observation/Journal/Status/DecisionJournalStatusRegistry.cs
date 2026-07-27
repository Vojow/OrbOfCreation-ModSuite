using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;

internal sealed class DecisionJournalStatusRegistry : IDecisionJournalStatusSource
{
    private readonly int _ownerThreadId;
    private DecisionJournalStatusRegistration? _registration;
    private DecisionJournalStatus _status = DecisionJournalStatus.Unavailable;
    private long _revision;

    public DecisionJournalStatusRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    internal static DecisionJournalStatusRegistry Shared { get; } = new();

    public DecisionJournalStatus Status
    {
        get
        {
            AssertOwnerThread();
            return _status;
        }
    }

    public long Revision
    {
        get
        {
            AssertOwnerThread();
            return _revision;
        }
    }

    internal DecisionJournalStatusRegistration Register()
    {
        if (!TryRegister(out var registration) || registration is null)
            throw new InvalidOperationException("A decision-journal status producer is already registered.");
        return registration;
    }

    internal bool TryRegister(out DecisionJournalStatusRegistration? registration)
    {
        AssertOwnerThread();
        if (_registration is not null)
        {
            registration = null;
            return false;
        }
        registration = new DecisionJournalStatusRegistration(this);
        _registration = registration;
        return true;
    }

    internal bool Publish(DecisionJournalStatusRegistration registration, DecisionJournalStatus status)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (status.State == DecisionJournalStatusState.Unavailable)
            throw new ArgumentException("Only the registry may publish unavailable state.", nameof(status));
        if (_status == status) return false;
        _status = status;
        AdvanceRevision();
        return true;
    }

    internal void Remove(DecisionJournalStatusRegistration registration)
    {
        AssertOwnerThread();
        if (!ReferenceEquals(_registration, registration)) return;
        _registration = null;
        _status = DecisionJournalStatus.Unavailable;
        AdvanceRevision();
    }

    private void AssertRegistration(DecisionJournalStatusRegistration registration)
    {
        if (!ReferenceEquals(_registration, registration))
            throw new ObjectDisposedException(nameof(DecisionJournalStatusRegistration));
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Decision-journal status must remain on its owning main thread.");
    }
}
