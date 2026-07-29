using OrbAutomata;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// World snapshots for tests that consume one.
/// </summary>
/// <remarks>
/// <see cref="FromLoadedRegistries"/> deliberately runs the real collector rather than hand-building
/// a snapshot. A consuming service's test is then an end-to-end one — stub registries through
/// collection, derivation and publication into the consumer — which is the flow that actually has to
/// work. A hand-built world would agree with whatever the consumer expected and prove nothing about
/// the pass that fills it.
/// </remarks>
internal static class TestWorlds
{
    internal static GameWorldState Empty => GameWorldStateDefaults.Empty;

    internal static GameWorldState FromLoadedRegistries()
    {
        var collector = new GameWorldCollector();
        collector.Collect();
        return collector.Build();
    }
}
