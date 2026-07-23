using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Translates consumed configuration and strategy generations into causal facts.</summary>
internal sealed class ServiceCycleSemanticPublicationEvents
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private readonly ServiceCycleSemanticTraceState _state;

    internal ServiceCycleSemanticPublicationEvents(
        ServiceCycleSemanticRecorder recorder,
        ServiceCycleSemanticTraceState state)
    {
        _recorder = recorder;
        _state = state;
    }

    internal void Bind(
        int ordinal,
        ServiceId service,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        _recorder.RegisterService(ordinal, service);
        EnsureConfiguration(ordinal, configuration, observedAt);
        EnsureStrategy(ordinal, strategy, observedAt);
    }

    internal void Observe(
        int ordinal,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        EnsureConfiguration(ordinal, configuration, observedAt);
        EnsureStrategy(ordinal, strategy, observedAt);
    }

    internal void EnsureConfiguration(
        int ordinal,
        ConfigGeneration generation,
        MonotonicTimestamp observedAt)
    {
        ref var cursor = ref _state.For(ordinal);
        if (!generation.IsValid ||
            generation.Value <= cursor.ConfigurationPublicationHighWater.Value) return;
        _recorder.ConfigurationPublished(ordinal, generation, observedAt);
        cursor.ConfigurationPublicationHighWater = generation;
    }

    internal void EnsureStrategy(
        int ordinal,
        StrategyGeneration generation,
        MonotonicTimestamp observedAt)
    {
        ref var cursor = ref _state.For(ordinal);
        if (generation.Value == 0 ||
            generation.Value <= cursor.StrategyPublicationHighWater.Value) return;
        _recorder.StrategyPublished(ordinal, generation, observedAt);
        cursor.StrategyPublicationHighWater = generation;
    }
}
