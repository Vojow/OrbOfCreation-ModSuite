using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// Stands in for the collection service, which in the game reads the world every frame and publishes
/// it stamped with the frame it read.
/// </summary>
/// <remarks>
/// A service that has changed the game does not start another cycle until a reading later than that
/// change arrives, and nothing opts into that. A test composition with no collector is the one
/// situation the game never has, so tests say what the collector would have said instead of the
/// runtime pretending the rule does not apply to them.
/// </remarks>
internal static class TestWorldCollector
{
    /// <summary>
    /// The frame the first reading after the publisher's seed is stamped with. Older than any frame
    /// a fixture goes on to pump, so a test that collects again later still publishes something
    /// newer.
    /// </summary>
    internal const long ActivationFrame = 2;

    /// <summary>
    /// Publishes the first reading a composition's services are allowed to act on.
    /// </summary>
    /// <remarks>
    /// The gate is born armed: a mutating service waits for a world collected strictly after it went
    /// live, so a composition whose collector never speaks starts no cycle at all. In the game the
    /// collector has read the save long before anything acts; a fixture says the same thing once,
    /// here, rather than the runtime making an exception for fixtures.
    /// </remarks>
    internal static void CollectedAtActivation(ServiceCycleRegistry registry) =>
        CollectedAt(registry, ActivationFrame);

    /// <summary>
    /// Publishes the suite's world as collected on <paramref name="frameIdentity"/>. Only the
    /// generation matters to the freshness gate, so the snapshot is the empty world unless a test
    /// needs otherwise.
    /// </summary>
    internal static void CollectedAt(ServiceCycleRegistry registry, long frameIdentity) =>
        CollectedAt(registry, frameIdentity, GameWorldStateDefaults.Empty);

    internal static void CollectedAt(
        ServiceCycleRegistry registry,
        long frameIdentity,
        GameWorldState world) =>
        registry.WorldPublication.Publish(world, new WorldGeneration(checked((ulong)frameIdentity)));
}
