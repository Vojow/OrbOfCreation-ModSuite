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
        // The publications go last, and the order is load-bearing: a runner reads its configuration
        // generation as it shuts down, so disposing the publication before the slots that own the
        // runners would raise ObjectDisposedException on shutdown and nowhere else.
        try { _world.Dispose(); }
        catch (Exception ex) { firstFailure ??= ex; }
        try { _configuration.Dispose(); }
        catch (Exception ex) { firstFailure ??= ex; }
        try { _strategy.Dispose(); }
        catch (Exception ex) { firstFailure ??= ex; }
        if (firstFailure is not null) throw firstFailure;
    }
}
