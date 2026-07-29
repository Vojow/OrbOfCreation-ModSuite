using System;

namespace OrbAutomata;

internal sealed class AutoItemsServiceAdapterComposition
{
    private AutoItemsServiceAdapterComposition(
        IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> definition,
        AutoItemsNativeAdapter natives,
        AutoItemsTemporaryActivationTracker temporaryActivations)
    {
        Definition = definition;
        Natives = natives;
        TemporaryActivations = temporaryActivations;
    }

    internal IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Definition { get; }
    internal AutoItemsNativeAdapter Natives { get; }
    internal AutoItemsTemporaryActivationTracker TemporaryActivations { get; }

    internal static AutoItemsServiceAdapterComposition Create(
        AutoItemsFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var temporaryActivations = new AutoItemsTemporaryActivationTracker();
        var natives = new AutoItemsNativeAdapter(
            dependencies.RegistryResolver,
            dependencies.TryCaptureMutationPermit,
            temporaryActivations);
        var actions = new AutoItemsCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily);
        return new AutoItemsServiceAdapterComposition(
            AutoItemsService.Define(actions, temporaryActivations),
            natives,
            temporaryActivations);
    }
}
