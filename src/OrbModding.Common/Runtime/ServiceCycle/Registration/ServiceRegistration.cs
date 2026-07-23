using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed class ServiceRegistration<TFrame, TConfig, TState, TAction> : IDisposable
    where TConfig : notnull
{
    private ServiceCycleRegistry? _registry;
    private ServiceCycleSlot<TFrame, TConfig, TState, TAction>? _slot;
    private readonly int _ordinal;

    internal ServiceRegistration(
        ServiceCycleRegistry registry,
        ServiceCycleSlot<TFrame, TConfig, TState, TAction> slot)
    {
        _registry = registry;
        _slot = slot;
        _ordinal = slot.Ordinal;
    }

    public int Ordinal => _ordinal;
    public ServiceConfigurationPublisher<TConfig> Configuration =>
        _slot?.Configuration ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
    internal ServiceRunner<TFrame, TConfig, TState, TAction> Runner =>
        _slot?.Runner ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
    internal ServiceCycleSlot<TFrame, TConfig, TState, TAction> Slot =>
        _slot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
    internal ServiceLifecycleSlotSnapshot LifecycleSnapshot =>
        _slot?.LifecycleSnapshot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));

    /// <summary>
    /// Offline deterministic-tooling boundary. Gameplay frame pumping never waits for workers.
    /// </summary>
    internal bool WaitForResponseReady(TimeSpan timeout) => Runner.WaitForResponseReady(timeout);

    internal bool WaitForResponseReady(ServiceCycleIdentity expectedCycle, TimeSpan timeout) =>
        (_slot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>)))
        .WaitForResponseReady(expectedCycle, timeout);

    public void BindStrategy<TStrategy>(ServiceStrategyPublisher<TStrategy> strategy)
        where TStrategy : notnull
    {
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        var registry = _registry ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
        var slot = _slot ?? throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
        registry.BindStrategy(slot, strategy);
    }

    /// <summary>
    /// Internal composition seam for deterministic tooling that owns only generation evidence rather
    /// than a feature bulletin value. Product composition continues to use the strongly typed public
    /// publisher overload.
    /// </summary>
    internal void BindStrategyGenerationSource(IServiceStrategyGenerationSource strategy)
    {
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        var registry = _registry ??
            throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
        var slot = _slot ??
            throw new ObjectDisposedException(nameof(ServiceRegistration<TFrame, TConfig, TState, TAction>));
        registry.BindStrategy(slot, strategy);
    }

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
