using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

public sealed class ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction> : IDisposable
    where TConfig : notnull
{
    private ServiceRegistration<TFrame, TConfig, TState, TAction>? _ordinary;

    internal ServiceCycleReplayRegistration(
        ServiceRegistration<TFrame, TConfig, TState, TAction> ordinary,
        ServiceCycleReplaySession recording)
    {
        _ordinary = ordinary ?? throw new ArgumentNullException(nameof(ordinary));
        Recording = recording ?? throw new ArgumentNullException(nameof(recording));
    }

    public int Ordinal => _ordinary?.Ordinal ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>));
    public ServiceConfigurationPublisher<TConfig> Configuration => _ordinary?.Configuration ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>));
    public ServiceCycleReplaySession Recording { get; }
    internal ServiceRunner<TFrame, TConfig, TState, TAction> Runner => (_ordinary ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
        .Runner;
    internal ServiceCycleSlot<TFrame, TConfig, TState, TAction> Slot => (_ordinary ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
        .Slot;
    internal bool WaitForResponseReady(ServiceCycleIdentity expectedCycle, TimeSpan timeout) => (_ordinary ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
        .WaitForResponseReady(expectedCycle, timeout);
    internal bool WaitForResponseReady(TimeSpan timeout) => (_ordinary ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
        .Runner.WaitForResponseReady(timeout);

    public void BindStrategy<TStrategy>(ServiceStrategyPublisher<TStrategy> strategy)
        where TStrategy : notnull => (_ordinary ??
            throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
            .BindStrategy(strategy);

    internal void BindStrategyGenerationSource(IServiceStrategyGenerationSource strategy) => (_ordinary ??
        throw new ObjectDisposedException(nameof(ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>)))
        .BindStrategyGenerationSource(strategy);

    public void Dispose()
    {
        var ordinary = _ordinary;
        if (ordinary is null) return;
        ordinary.Dispose();
        _ordinary = null;
    }
}

public static class ServiceCycleReplayRegistrationExtensions
{
    public static ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction> RegisterReplay<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        this ServiceCycleRegistry registry,
        IServiceCycleReplayDefinition<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> definition,
        TConfig initialConfiguration,
        ServiceCycleReplaySession recording)
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (registry.CurrentLifecycle.Value == 0)
            throw new InvalidOperationException("The registry requires an initial lifecycle before replay registration.");
        return RegisterCore(
            registry,
            definition,
            initialConfiguration,
            recording,
            registry.CurrentLifecycle,
            useExplicitLifecycle: false
#if SERVICE_CYCLE_PROFILE
            , profileProbe: null
#endif
            );
    }

    public static ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction> RegisterReplay<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        this ServiceCycleRegistry registry,
        IServiceCycleReplayDefinition<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> definition,
        TConfig initialConfiguration,
        ServiceCycleReplaySession recording,
        LifecycleGeneration lifecycle)
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord => RegisterCore(
            registry,
            definition,
            initialConfiguration,
            recording,
            lifecycle,
            useExplicitLifecycle: true
#if SERVICE_CYCLE_PROFILE
            , profileProbe: null
#endif
            );

#if SERVICE_CYCLE_PROFILE
    internal static ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction> RegisterReplayProfiled<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        this ServiceCycleRegistry registry,
        IServiceCycleReplayDefinition<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> definition,
        TConfig initialConfiguration,
        ServiceCycleReplaySession recording,
        ServiceCycleProfileProbe profileProbe)
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (registry.CurrentLifecycle.Value == 0)
            throw new InvalidOperationException("The registry requires an initial lifecycle before replay registration.");
        return RegisterCore(
            registry,
            definition,
            initialConfiguration,
            recording,
            registry.CurrentLifecycle,
            useExplicitLifecycle: false,
            profileProbe);
    }
#endif

    private static ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction> RegisterCore<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>(
        ServiceCycleRegistry registry,
        IServiceCycleReplayDefinition<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> definition,
        TConfig initialConfiguration,
        ServiceCycleReplaySession recording,
        LifecycleGeneration lifecycle,
        bool useExplicitLifecycle
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe? profileProbe
#endif
        )
        where TConfig : notnull
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (recording is null) throw new ArgumentNullException(nameof(recording));
        var adapter = new ServiceCycleReplayDefinitionAdapter<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord>(
                registry,
                definition,
                recording,
                lifecycle
#if SERVICE_CYCLE_PROFILE
                , profileProbe ?? new ServiceCycleProfileProbe()
#endif
                );
        ServiceRegistration<TFrame, TConfig, TState, TAction>? ordinary = null;
        try
        {
            ordinary = useExplicitLifecycle
                ? registry.Register(adapter, initialConfiguration, lifecycle)
                : registry.Register(adapter, initialConfiguration);
            adapter.BindTraceServiceKey(checked(ordinary.Ordinal + 1));
            return new ServiceCycleReplayRegistration<TFrame, TConfig, TState, TAction>(ordinary, recording);
        }
        catch
        {
            try { ordinary?.Dispose(); }
            finally { adapter.RollBackConstruction(); }
            throw;
        }
    }
}
