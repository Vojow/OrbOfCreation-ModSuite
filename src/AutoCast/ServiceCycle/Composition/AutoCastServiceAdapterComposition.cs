using System;

namespace OrbAutomata;

/// <summary>
/// Builds the Auto Cast action adapter and the plain typed service definition from the feature's
/// dependencies. There is no capture adapter: the runtime pins the world and the worker reads it, so
/// nothing is left for capture to do.
/// </summary>
internal sealed class AutoCastServiceAdapterComposition
{
    private AutoCastServiceAdapterComposition(
        IAutomataServiceDefinition<
            AutoCastCycleState,
            AutoCastCycleAction> definition,
        AutoCastNativeAdapter natives)
    {
        Definition = definition;
        Natives = natives;
    }

    internal IAutomataServiceDefinition<
        AutoCastCycleState,
        AutoCastCycleAction> Definition { get; }

    /// <summary>
    /// The one native adapter. It owns the bound cast contracts and the per-spell quarantine, and a
    /// spell the boundary blocked must stay blocked until the next lifecycle whoever asks next.
    /// </summary>
    internal AutoCastNativeAdapter Natives { get; }

    internal static AutoCastServiceAdapterComposition Create(AutoCastFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var natives = new AutoCastNativeAdapter();
        var actions = new AutoCastCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            dependencies.ManualPause);
        return new AutoCastServiceAdapterComposition(
            AutoCastService.Define(actions, dependencies.ManualPause), natives);
    }
}
