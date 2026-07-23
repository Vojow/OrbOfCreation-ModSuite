using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    public ServiceRegistration<TFrame, TConfig, TState, TAction> Register<TFrame, TConfig, TState, TAction>(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        TConfig initialConfiguration)
        where TConfig : notnull
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (!_hasLifecycle)
            throw new InvalidOperationException(
                "The registry requires a centralized initial lifecycle before registration without an explicit generation.");
        return Register(definition, initialConfiguration, _lifecycle);
    }

    public ServiceRegistration<TFrame, TConfig, TState, TAction> Register<TFrame, TConfig, TState, TAction>(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        TConfig initialConfiguration,
        RuntimeLifecycleGeneration lifecycle)
        where TConfig : notnull
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (_sealed)
            throw new InvalidOperationException("The service-cycle composition is sealed.");
        ServiceCycleTypeSafetyValidator.EnsureServiceTypes<TFrame, TConfig, TState, TAction>();
        var serviceId = definition.ServiceId;
        var defaultWakePolicy = definition.DefaultWakePolicy;
        var faultRecoveryPolicy = definition.FaultRecoveryPolicy;
        if (!serviceId.IsValid)
            throw new ArgumentException("The definition requires a valid service identity.", nameof(definition));
        if (_activeCount == _slots.Length)
            throw new InvalidOperationException($"The registry supports at most {_slots.Length} services.");
        if (_byServiceId.ContainsKey(serviceId))
            throw new InvalidOperationException($"Service '{serviceId}' is already registered.");
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (_hasLifecycle && lifecycle != _lifecycle)
            throw new InvalidOperationException(
                "Every registration must use the registry's centralized lifecycle generation.");

        ServiceCycleSlot<TFrame, TConfig, TState, TAction>? slot = null;
        ServiceConfigurationPublisher<TConfig>? configuration = null;
        var ordinal = FindAvailableOrdinal();
        var ordinalCount = _nextOrdinal;
        var activeCount = _activeCount;
        var ordinalOwner = ordinal < ordinalCount ? _slots[ordinal] : null;
        try
        {
            configuration = new ServiceConfigurationPublisher<TConfig>(initialConfiguration);
            var token = checked(++_nextRegistrationToken);
            _constructingRunner = true;
            try
            {
                slot = new ServiceCycleSlot<TFrame, TConfig, TState, TAction>(
                    token,
                    ordinal,
                    definition,
                    serviceId,
                    defaultWakePolicy,
                    faultRecoveryPolicy,
                    configuration,
                    lifecycle,
                    _clock,
                    _measureWorkerAllocations,
                    _workerStarter,
                    _workerExitObserver,
                    _resourceClaims);
            }
            finally
            {
                _constructingRunner = false;
            }
            ValidateRegistrationPublication(
                serviceId,
                ordinal,
                ordinalCount,
                activeCount,
                ordinalOwner);
            var registration = new ServiceRegistration<TFrame, TConfig, TState, TAction>(this, slot);
            _byServiceId.Add(slot.ServiceId, slot);
            _slots[ordinal] = slot;
            if (ordinal == _nextOrdinal) _nextOrdinal++;
            _activeCount++;
            if (!_hasLifecycle)
            {
                _lifecycle = lifecycle;
                _hasLifecycle = true;
            }
            return registration;
        }
        catch
        {
            try { slot?.Dispose(); }
            catch { }
            if (slot is null)
            {
                try { configuration?.Dispose(); }
                catch { }
            }
            throw;
        }
    }

    internal void BindStrategy(IServiceCycleSlot slot, IServiceStrategyGenerationSource strategy)
    {
        if (slot is null) throw new ArgumentNullException(nameof(slot));
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (_sealed)
            throw new InvalidOperationException("Strategy sources must be bound before composition is sealed.");
        if ((uint)slot.Ordinal >= (uint)_nextOrdinal ||
            !ReferenceEquals(_slots[slot.Ordinal], slot) ||
            slot.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(slot));
        }
        slot.BindStrategy(strategy);
    }

    private int FindAvailableOrdinal()
    {
        for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
        {
            if (_slots[ordinal].IsDisposed) return ordinal;
        }
        return _nextOrdinal;
    }

    private void ValidateRegistrationPublication(
        ServiceId serviceId,
        int ordinal,
        int ordinalCount,
        int activeCount,
        IServiceCycleSlot? ordinalOwner)
    {
        if (_disposed || _sealed || _activeCount != activeCount || _nextOrdinal != ordinalCount ||
            _byServiceId.ContainsKey(serviceId) ||
            (ordinal < ordinalCount && !ReferenceEquals(_slots[ordinal], ordinalOwner)) ||
            (ordinal == ordinalCount && ordinal != _nextOrdinal))
        {
            throw new InvalidOperationException(
                "Service-cycle composition changed while a runner was being constructed.");
        }
    }
}
