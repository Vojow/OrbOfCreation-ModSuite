using System;

namespace OrbAutomata;

internal sealed class AutoAgromancyServiceAdapterComposition
{
    private AutoAgromancyServiceAdapterComposition(
        IAutomataServiceDefinition<AutoAgromancyCycleState, AutoAgromancyCycleAction> definition,
        AutoAgromancyCycleActionAdapter actions)
    {
        Definition = definition;
        Actions = actions;
    }

    internal IAutomataServiceDefinition<
        AutoAgromancyCycleState,
        AutoAgromancyCycleAction> Definition { get; }
    internal AutoAgromancyCycleActionAdapter Actions { get; }

    internal static AutoAgromancyServiceAdapterComposition Create(
        AutoAgromancyFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var actions = new AutoAgromancyCycleActionAdapter(
            new AutoAgromancyNativeAdapter(
                dependencies.TryCaptureMutationPermit),
            new AutoAgromancyLiveWorldReader(dependencies.CreateLiveCollector()),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            dependencies.TryCaptureMutationPermit,
            dependencies.ReadConfiguration,
            dependencies.ReadConfigurationGeneration);
        return new AutoAgromancyServiceAdapterComposition(
            AutoAgromancyService.Define(actions),
            actions);
    }
}
