using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// One main-thread capture of the shared immutable world and the neutral coordinates needed by
/// read-only observers outside the ServiceCycle runtime.
/// </summary>
internal readonly struct AutomataPublishedWorldCapture
{
    internal AutomataPublishedWorldCapture(
        GameWorldState world,
        WorldGeneration worldGeneration,
        LifecycleGeneration lifecycleGeneration,
        MonotonicTimestamp observedAt)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        if (!worldGeneration.IsValid)
            throw new ArgumentException("A valid world generation is required.", nameof(worldGeneration));
        WorldGeneration = worldGeneration;
        LifecycleGeneration = lifecycleGeneration;
        ObservedAt = observedAt;
    }

    internal GameWorldState World { get; }
    internal WorldGeneration WorldGeneration { get; }
    internal LifecycleGeneration LifecycleGeneration { get; }
    internal MonotonicTimestamp ObservedAt { get; }
}
