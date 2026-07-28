using System;

namespace OrbAutomata;

/// <summary>
/// Builds the Spell Leveling action adapter and the plain typed service definition from the feature's
/// dependencies. There is no capture adapter: the runtime pins the world and the worker reads it, so
/// nothing is left for capture to do.
/// </summary>
internal sealed class SpellLevelServiceAdapterComposition
{
    private SpellLevelServiceAdapterComposition(
        IAutomataServiceDefinition<
            SpellLevelCycleState,
            SpellLevelCycleAction> definition,
        SpellLevelNativeAdapter natives)
    {
        Definition = definition;
        Natives = natives;
    }

    internal IAutomataServiceDefinition<
        SpellLevelCycleState,
        SpellLevelCycleAction> Definition { get; }

    /// <summary>
    /// The one native adapter, shared by the action boundary and the capability probe. Sharing it is
    /// the point: both need the same bound contracts, and a contract the boundary blocked must stay
    /// blocked for the probe too.
    /// </summary>
    internal SpellLevelNativeAdapter Natives { get; }

    internal static SpellLevelServiceAdapterComposition Create(SpellLevelFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var natives = new SpellLevelNativeAdapter();
        var actions = new SpellLevelCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            dependencies.Capability.Observe);
        return new SpellLevelServiceAdapterComposition(SpellLevelService.Define(actions), natives);
    }
}
