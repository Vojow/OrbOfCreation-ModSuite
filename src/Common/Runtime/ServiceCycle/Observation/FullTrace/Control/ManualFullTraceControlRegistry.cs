using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Control;

public sealed class ManualFullTraceControlRegistry : IManualFullTraceControl
{
    private readonly int _ownerThreadId;
    private ManualFullTraceControlRegistration? _registration;
    private ManualFullTraceStatus _status = ManualFullTraceStatus.Unavailable;
    private ManualFullTraceCommand _pendingCommand;
    private long _revision;

    public ManualFullTraceControlRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static ManualFullTraceControlRegistry Shared { get; } = new();

    public ManualFullTraceStatus Status
    {
        get
        {
            AssertOwnerThread();
            return _status;
        }
    }

    public ManualFullTraceCommand PendingCommand
    {
        get
        {
            AssertOwnerThread();
            return _pendingCommand;
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

    public ManualFullTraceControlRegistration Register()
    {
        if (!TryRegister(out var registration) || registration is null)
            throw new InvalidOperationException("A manual full-trace producer is already registered.");
        return registration;
    }

    public bool TryRegister(out ManualFullTraceControlRegistration? registration)
    {
        AssertOwnerThread();
        if (_registration is not null)
        {
            registration = null;
            return false;
        }
        registration = new ManualFullTraceControlRegistration(this);
        _registration = registration;
        SetStatus(ManualFullTraceStatus.Idle);
        return true;
    }

    public ManualFullTraceCommandResult RequestStart() => Request(ManualFullTraceCommand.Start);

    public ManualFullTraceCommandResult RequestStop() => Request(ManualFullTraceCommand.Stop);

    internal bool Publish(
        ManualFullTraceControlRegistration registration,
        ManualFullTraceStatus status)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (status.State == ManualFullTraceState.Unavailable)
            throw new ArgumentException("Only the registry may publish unavailable state.", nameof(status));
        return SetStatus(status);
    }

    internal bool TryTakeCommand(
        ManualFullTraceControlRegistration registration,
        out ManualFullTraceCommand command)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        command = _pendingCommand;
        if (command == ManualFullTraceCommand.None) return false;
        _pendingCommand = ManualFullTraceCommand.None;
        AdvanceRevision();
        return true;
    }

    internal void Remove(ManualFullTraceControlRegistration registration)
    {
        AssertOwnerThread();
        if (!ReferenceEquals(_registration, registration)) return;
        _registration = null;
        _pendingCommand = ManualFullTraceCommand.None;
        SetStatus(ManualFullTraceStatus.Unavailable);
    }

    private ManualFullTraceCommandResult Request(ManualFullTraceCommand command)
    {
        AssertOwnerThread();
        if (_registration is null) return ManualFullTraceCommandResult.Unavailable;
        if (_pendingCommand != ManualFullTraceCommand.None)
            return ManualFullTraceCommandResult.CommandPending;

        var valid = command switch
        {
            ManualFullTraceCommand.Start => _status.State is ManualFullTraceState.Idle or
                ManualFullTraceState.Complete or ManualFullTraceState.Incomplete,
            ManualFullTraceCommand.Stop => _status.State is ManualFullTraceState.Arming or
                ManualFullTraceState.Recording,
            _ => false,
        };
        if (!valid) return ManualFullTraceCommandResult.InvalidState;
        _pendingCommand = command;
        AdvanceRevision();
        return ManualFullTraceCommandResult.Accepted;
    }

    private bool SetStatus(ManualFullTraceStatus status)
    {
        if (_status == status) return false;
        _status = status;
        AdvanceRevision();
        return true;
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private void AssertRegistration(ManualFullTraceControlRegistration registration)
    {
        if (!ReferenceEquals(_registration, registration))
            throw new ObjectDisposedException(nameof(ManualFullTraceControlRegistration));
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Manual full-trace control must remain on its owning main thread.");
    }
}
