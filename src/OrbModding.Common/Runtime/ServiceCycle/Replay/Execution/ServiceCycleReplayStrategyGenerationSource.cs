using System;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Artifact-controlled generation source used to exercise the same registration and publication
/// observation seam as a production strategy publisher without inventing bulletin values.
/// </summary>
internal sealed class ServiceCycleReplayStrategyGenerationSource : IServiceStrategyGenerationSource
{
    private ulong _generation;

    internal ServiceCycleReplayStrategyGenerationSource(ulong initialGeneration) =>
        _generation = initialGeneration;

    internal ulong Generation => _generation;

    internal void AdvanceTo(ulong generation)
    {
        if (generation == 0 || generation <= _generation)
            throw new InvalidOperationException("Replay strategy generation did not advance.");
        _generation = generation;
    }

    bool IServiceStrategyGenerationSource.TryGetLatestGeneration(out StrategyGeneration generation)
    {
        generation = new StrategyGeneration(_generation);
        return _generation != 0;
    }
}
