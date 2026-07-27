using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed class ServiceRegistration<TState, TAction> : IDisposable
{
    private ServiceCycleRegistry? _registry;
    private ServiceCycleSlot<TState, TAction>? _slot;
    private readonly int _ordinal;

    internal ServiceRegistration(
        ServiceCycleRegistry registry,
        ServiceCycleSlot<TState, TAction> slot)
    {
        _registry = registry;
        _slot = slot;
        _ordinal = slot.Ordinal;
    }

    public int Ordinal => _ordinal;
    internal ServiceRunner<TState, TAction> Runner =>
        _slot?.Runner ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TState, TAction>));
    internal ServiceCycleSlot<TState, TAction> Slot =>
        _slot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TState, TAction>));
    internal ServiceLifecycleSlotSnapshot LifecycleSnapshot =>
        _slot?.LifecycleSnapshot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TState, TAction>));

    /// <summary>
    /// Offline deterministic-tooling boundary. Gameplay frame pumping never waits for workers.
    /// </summary>
    internal bool WaitForResponseReady(TimeSpan timeout) => Runner.WaitForResponseReady(timeout);

    internal bool WaitForResponseReady(ServiceCycleIdentity expectedCycle, TimeSpan timeout) =>
        (_slot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TState, TAction>)))
        .WaitForResponseReady(expectedCycle, timeout);

    public void Dispose()
    {
        var registry = _registry;
        var slot = _slot;
        if (registry is null || slot is null) return;
        try
        {
            registry.Release(slot);
            _slot = null;
            _registry = null;
        }
        catch
        {
            if (slot.IsDisposed)
            {
                _slot = null;
                _registry = null;
            }
            throw;
        }
    }
}
