using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>
/// Translates consumed configuration and strategy generations into causal facts.
/// </summary>
/// <remarks>
/// One high-water pair for the whole suite, not one per service. The suite has a single configuration
/// record and a single strategy bulletin that every service reads, so a publication is one fact;
/// tracking it per ordinal emitted the same fact once per registered service.
/// </remarks>
internal sealed class ServiceCycleSemanticPublicationEvents
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private ConfigGeneration _configurationHighWater;
    private StrategyGeneration _strategyHighWater;

    internal ServiceCycleSemanticPublicationEvents(ServiceCycleSemanticRecorder recorder) =>
        _recorder = recorder;

    internal void Bind(
        int ordinal,
        ServiceId service,
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        _recorder.RegisterService(ordinal, service);
        Observe(configuration, strategy, observedAt);
    }

    internal void Observe(
        ConfigGeneration configuration,
        StrategyGeneration strategy,
        MonotonicTimestamp observedAt)
    {
        EnsureConfiguration(configuration, observedAt);
        EnsureStrategy(strategy, observedAt);
    }

    internal void EnsureConfiguration(ConfigGeneration generation, MonotonicTimestamp observedAt)
    {
        if (!generation.IsValid || generation.Value <= _configurationHighWater.Value) return;
        _recorder.ConfigurationPublished(generation, observedAt);
        _configurationHighWater = generation;
    }

    internal void EnsureStrategy(StrategyGeneration generation, MonotonicTimestamp observedAt)
    {
        if (generation.Value == 0 || generation.Value <= _strategyHighWater.Value) return;
        _recorder.StrategyPublished(generation, observedAt);
        _strategyHighWater = generation;
    }
}
