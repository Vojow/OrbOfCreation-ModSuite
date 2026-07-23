using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    public void Seal()
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        _sealed = true;
    }

    public ServiceId GetServiceId(int ordinal)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if ((uint)ordinal >= (uint)_nextOrdinal)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _slots[ordinal].ServiceId;
    }

    internal IServiceCycleSlot GetSlot(int ordinal)
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if ((uint)ordinal >= (uint)_nextOrdinal)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _slots[ordinal];
    }

    internal void AssertDiagnosticsRead()
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        if (_pumpCallbackDepth != 0 || _reconcilingLifecycle || _constructingRunner)
            throw new InvalidOperationException(
                "Service-cycle diagnostics cannot be projected from a service or lifecycle callback.");
    }

    internal int ClaimPump()
    {
        AssertOwnerThread();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (!_sealed)
            throw new InvalidOperationException("The service-cycle composition must be sealed before its pump is created.");
        if (_pumpClaimed)
            throw new InvalidOperationException("The sealed service-cycle registry already has a frame pump.");
        _pumpClaimed = true;
        return _nextOrdinal;
    }

    internal void EnterPumpCallback()
    {
        AssertOwnerThread();
        _pumpCallbackDepth = checked(_pumpCallbackDepth + 1);
    }

    internal void ExitPumpCallback()
    {
        AssertOwnerThread();
        if (_pumpCallbackDepth <= 0)
            throw new InvalidOperationException("No service-cycle pump callback is active.");
        _pumpCallbackDepth--;
    }
}
