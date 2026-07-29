using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

public sealed partial class ServiceCycleRegistry
{
    public ServiceRegistration<TState, TAction>
        Register<TState, TAction>(
            IServiceCycleDefinition<TState, TAction> definition)
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (!_hasLifecycle)
            throw new InvalidOperationException(
                "The registry requires a centralized initial lifecycle before registration without an explicit generation.");
        return Register(definition, _lifecycle, ServiceActionDispatchPolicy.Single);
    }

    public ServiceRegistration<TState, TAction>
        Register<TState, TAction>(
            IServiceCycleDefinition<TState, TAction> definition,
            ServiceActionDispatchPolicy actionDispatchPolicy)
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (!_hasLifecycle)
            throw new InvalidOperationException(
                "The registry requires a centralized initial lifecycle before registration without an explicit generation.");
        return Register(definition, _lifecycle, actionDispatchPolicy);
    }

    public ServiceRegistration<TState, TAction>
        Register<TState, TAction>(
            IServiceCycleDefinition<TState, TAction> definition,
            RuntimeLifecycleGeneration lifecycle) =>
        Register(definition, lifecycle, ServiceActionDispatchPolicy.Single);

    public ServiceRegistration<TState, TAction>
        Register<TState, TAction>(
            IServiceCycleDefinition<TState, TAction> definition,
            RuntimeLifecycleGeneration lifecycle,
            ServiceActionDispatchPolicy actionDispatchPolicy)
    {
        var parts = PrepareRegistration<TState, TAction>(
            definition, lifecycle, actionDispatchPolicy);
        return Complete(
            new ServiceOrdinaryRunnerFactory<TState, TAction>(
                definition, in parts),
            in parts,
            lifecycle,
            actionDispatchPolicy);
    }

    /// <summary>
    /// Registers the one service that reads the game and publishes what it read, at the registry's
    /// own lifecycle generation.
    /// </summary>
    internal ServiceRegistration<TState, TAction>
        RegisterSource<TState, TAction>(IServiceCycleSourceDefinition<TState, TAction> definition)
    {
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (!_hasLifecycle)
            throw new InvalidOperationException(
                "The registry requires a centralized initial lifecycle before registration without an explicit generation.");
        return RegisterSource(definition, _lifecycle);
    }

    /// <summary>
    /// Registers the one service that reads the game and publishes what it read.
    /// </summary>
    /// <remarks>
    /// The entry point supplies the dispatch policy rather than accepting one. A source publishes
    /// exactly one snapshot per frame; letting a caller name a policy would let the declaration and
    /// the shape disagree, and the runtime derives the shape from the policy. Taking the source
    /// contract is the declaration.
    /// </remarks>
    internal ServiceRegistration<TState, TAction>
        RegisterSource<TState, TAction>(
            IServiceCycleSourceDefinition<TState, TAction> definition,
            RuntimeLifecycleGeneration lifecycle)
    {
        var actionDispatchPolicy =
            ServiceActionDispatchPolicy.Bounded(1, ServiceActionDispatchClass.Publication);
        var parts = PrepareRegistration<TState, TAction>(
            definition, lifecycle, actionDispatchPolicy);
        return Complete(
            new ServiceSourceRunnerFactory<TState, TAction>(definition, in parts),
            in parts,
            lifecycle,
            actionDispatchPolicy);
    }

    /// <summary>
    /// Everything a registration has to be true of before a runner exists, and the collaborators the
    /// runner will be built from.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="Complete"/> so the caller can name the factory it wants between the two.
    /// Handing the choice in as a delegate would put a stored delegate on the registration path, and
    /// the boundary rule that forbids that is what keeps dispatch out of this layer.
    /// </remarks>
    private ServiceRunnerFactoryParts
        PrepareRegistration<TState, TAction>(
            IServiceCycleMainThreadDefinition<TAction> definition,
            RuntimeLifecycleGeneration lifecycle,
            ServiceActionDispatchPolicy actionDispatchPolicy)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        AssertOwnerThread();
        ThrowIfPumpCallback();
        ThrowIfDisposed();
        ThrowIfRunnerConstruction();
        if (_sealed)
            throw new InvalidOperationException("The service-cycle composition is sealed.");
        ServiceCycleTypeSafetyValidator
            .EnsureServiceTypes<TState, TAction>();
        var serviceId = definition.ServiceId;
        if (!serviceId.IsValid)
            throw new ArgumentException("The definition requires a valid service identity.", nameof(definition));
        if (!actionDispatchPolicy.IsValid)
            throw new ArgumentException(
                "A valid action dispatch policy is required.",
                nameof(actionDispatchPolicy));
        if (_activeCount == _slots.Length)
            throw new InvalidOperationException($"The registry supports at most {_slots.Length} services.");
        if (_byServiceId.ContainsKey(serviceId))
            throw new InvalidOperationException($"Service '{serviceId}' is already registered.");
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (_hasLifecycle && lifecycle != _lifecycle)
            throw new InvalidOperationException(
                "Every registration must use the registry's centralized lifecycle generation.");

        return new ServiceRunnerFactoryParts(
            serviceId,
            definition.DefaultWakePolicy,
            definition.FaultRecoveryPolicy,
            _configuration,
            _clock,
            _measureWorkerAllocations,
            _resourceClaims,
            _workerStarter,
            _workerExitObserver,
            _strategy,
            _world);
    }

    private ServiceRegistration<TState, TAction> Complete<TState, TAction>(
        ServiceRunnerFactory<TState, TAction> factory,
        in ServiceRunnerFactoryParts parts,
        RuntimeLifecycleGeneration lifecycle,
        ServiceActionDispatchPolicy actionDispatchPolicy)
    {
        var serviceId = parts.ServiceId;
        var configuration = parts.Configuration;
        ServiceCycleSlot<TState, TAction>? slot = null;
        var ordinal = FindAvailableOrdinal();
        var ordinalCount = _nextOrdinal;
        var activeCount = _activeCount;
        var ordinalOwner = ordinal < ordinalCount ? _slots[ordinal] : null;
        try
        {
            var token = checked(++_nextRegistrationToken);
            _constructingRunner = true;
            try
            {
                slot = new ServiceCycleSlot<TState, TAction>(
                    token,
                    ordinal,
                    factory,
                    serviceId,
                    actionDispatchPolicy,
                    parts.DefaultWakePolicy,
                    parts.FaultRecoveryPolicy,
                    configuration,
                    lifecycle,
                    _strategy,
                    _world);
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
            var registration = new ServiceRegistration<TState, TAction>(this, slot);
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
            throw;
        }
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
