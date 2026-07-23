using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    internal void Release(IServiceCycleSlot slot)
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfRunnerConstruction();
        if (_disposed || slot.IsDisposed) return;

        if ((uint)slot.Ordinal >= (uint)_nextOrdinal) return;
        if (_slots[slot.Ordinal].RegistrationToken != slot.RegistrationToken) return;
        _byServiceId.Remove(slot.ServiceId);
        _activeCount--;
        try { slot.Dispose(); }
        finally
        {
            _slots[slot.Ordinal] = new ServiceCycleTombstone(
                slot.RegistrationToken,
                slot.Ordinal,
                slot.ServiceId,
                slot.LifecyclePositionTransitionCount,
                slot.LifecycleSnapshot);
        }
    }

    public void Dispose()
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfRunnerConstruction();
        if (_disposed) return;
        _disposed = true;
        Exception? firstFailure = null;
        for (var index = _nextOrdinal - 1; index >= 0; index--)
        {
            try { _slots[index].Dispose(); }
            catch (Exception ex) { firstFailure ??= ex; }
            _slots[index] = null!;
        }

        _byServiceId.Clear();
        _activeCount = 0;
        if (firstFailure is not null) throw firstFailure;
    }
}
