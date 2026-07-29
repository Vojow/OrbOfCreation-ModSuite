#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;

public sealed class PerformanceProfileControlRegistry : IPerformanceProfileControl
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private PerformanceProfileControlRegistration? _registration;
    private PerformanceProfileControlStatus _status = PerformanceProfileControlStatus.Unavailable;
    private PerformanceProfileCommand _pendingCommand;
    private long _revision;

    public static PerformanceProfileControlRegistry Shared { get; } = new();

    public PerformanceProfileControlStatus Status
    {
        get { AssertOwnerThread(); return _status; }
    }

    public PerformanceProfileCommand PendingCommand
    {
        get { AssertOwnerThread(); return _pendingCommand; }
    }

    public long Revision
    {
        get { AssertOwnerThread(); return _revision; }
    }

    public bool TryRegister(out PerformanceProfileControlRegistration? registration)
    {
        AssertOwnerThread();
        if (_registration is not null)
        {
            registration = null;
            return false;
        }
        registration = new PerformanceProfileControlRegistration(this);
        _registration = registration;
        SetStatus(PerformanceProfileControlStatus.Idle);
        return true;
    }

    public PerformanceProfileCommandResult RequestStart() => Request(PerformanceProfileCommand.Start);
    public PerformanceProfileCommandResult RequestStop() => Request(PerformanceProfileCommand.Stop);

    internal bool Publish(
        PerformanceProfileControlRegistration registration,
        PerformanceProfileControlStatus status)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (status.State == PerformanceProfileControlState.Unavailable)
            throw new ArgumentException("Only the registry may publish unavailable state.", nameof(status));
        return SetStatus(status);
    }

    internal bool TryTakeCommand(
        PerformanceProfileControlRegistration registration,
        out PerformanceProfileCommand command)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        command = _pendingCommand;
        if (command == PerformanceProfileCommand.None) return false;
        _pendingCommand = PerformanceProfileCommand.None;
        AdvanceRevision();
        return true;
    }

    internal void Remove(PerformanceProfileControlRegistration registration)
    {
        AssertOwnerThread();
        if (!ReferenceEquals(_registration, registration)) return;
        _registration = null;
        _pendingCommand = PerformanceProfileCommand.None;
        SetStatus(PerformanceProfileControlStatus.Unavailable);
    }

    private PerformanceProfileCommandResult Request(PerformanceProfileCommand command)
    {
        AssertOwnerThread();
        if (_registration is null) return PerformanceProfileCommandResult.Unavailable;
        if (_pendingCommand != PerformanceProfileCommand.None)
            return PerformanceProfileCommandResult.CommandPending;

        var valid = command switch
        {
            PerformanceProfileCommand.Start =>
                _status.State is PerformanceProfileControlState.Idle or PerformanceProfileControlState.Complete,
            PerformanceProfileCommand.Stop => _status.State == PerformanceProfileControlState.Recording,
            _ => false,
        };
        if (!valid) return PerformanceProfileCommandResult.InvalidState;
        _pendingCommand = command;
        AdvanceRevision();
        return PerformanceProfileCommandResult.Accepted;
    }

    private bool SetStatus(PerformanceProfileControlStatus status)
    {
        if (_status == status) return false;
        _status = status;
        AdvanceRevision();
        return true;
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private void AssertRegistration(PerformanceProfileControlRegistration registration)
    {
        if (!ReferenceEquals(_registration, registration))
            throw new ObjectDisposedException(nameof(PerformanceProfileControlRegistration));
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Performance-profile control must remain on its owning main thread.");
    }
}
#endif
