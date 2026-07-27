using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.HostTrace.Control;

public sealed class HostTraceDumpRegistry : IHostTraceDumpControl
{
    private readonly int _ownerThreadId;
    private HostTraceDumpRegistration? _registration;
    private HostTraceDumpStatus _status = HostTraceDumpStatus.Unavailable;
    private bool _dumpRequested;
    private long _revision;

    public HostTraceDumpRegistry() => _ownerThreadId = Thread.CurrentThread.ManagedThreadId;

    public static HostTraceDumpRegistry Shared { get; } = new();

    public HostTraceDumpStatus Status
    {
        get
        {
            AssertOwnerThread();
            return _status;
        }
    }

    public bool DumpRequested
    {
        get
        {
            AssertOwnerThread();
            return _dumpRequested;
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

    public bool TryRegister(out HostTraceDumpRegistration? registration)
    {
        AssertOwnerThread();
        if (_registration is not null)
        {
            registration = null;
            return false;
        }
        registration = new HostTraceDumpRegistration(this);
        _registration = registration;
        SetStatus(HostTraceDumpStatus.Idle);
        return true;
    }

    public HostTraceDumpRequestResult RequestDump()
    {
        AssertOwnerThread();
        if (_registration is null) return HostTraceDumpRequestResult.Unavailable;
        if (_dumpRequested) return HostTraceDumpRequestResult.RequestPending;
        _dumpRequested = true;
        AdvanceRevision();
        return HostTraceDumpRequestResult.Accepted;
    }

    internal bool Publish(HostTraceDumpRegistration registration, HostTraceDumpStatus status)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (status.State == HostTraceDumpState.Unavailable)
            throw new ArgumentException("Only the registry may publish unavailable state.", nameof(status));
        return SetStatus(status);
    }

    internal bool TryTakeRequest(HostTraceDumpRegistration registration)
    {
        AssertOwnerThread();
        AssertRegistration(registration);
        if (!_dumpRequested) return false;
        _dumpRequested = false;
        AdvanceRevision();
        return true;
    }

    internal void Remove(HostTraceDumpRegistration registration)
    {
        AssertOwnerThread();
        if (!ReferenceEquals(_registration, registration)) return;
        _registration = null;
        _dumpRequested = false;
        SetStatus(HostTraceDumpStatus.Unavailable);
    }

    private bool SetStatus(HostTraceDumpStatus status)
    {
        if (_status == status) return false;
        _status = status;
        AdvanceRevision();
        return true;
    }

    private void AdvanceRevision() => _revision = checked(_revision + 1);

    private void AssertRegistration(HostTraceDumpRegistration registration)
    {
        if (!ReferenceEquals(_registration, registration))
            throw new ObjectDisposedException(nameof(HostTraceDumpRegistration));
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Host-trace dump control must remain on its owning main thread.");
    }
}
