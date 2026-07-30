using System;

namespace OrbAutomata;

internal sealed class AutoItemsServiceAdapterComposition
{
    private AutoItemsServiceAdapterComposition(
        IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> definition,
        AutoItemsNativeAdapter natives)
    {
        Definition = definition;
        Natives = natives;
    }

    internal IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Definition { get; }
    internal AutoItemsNativeAdapter Natives { get; }

    internal static AutoItemsServiceAdapterComposition Create(
        AutoItemsFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var natives = new AutoItemsNativeAdapter(
            dependencies.RegistryResolver,
            dependencies.TryCaptureMutationPermit,
            dependencies.AutoScribeIdentityProfile);
        var actions = new AutoItemsCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily);
        return new AutoItemsServiceAdapterComposition(
            AutoItemsService.Define(
                actions,
                dependencies.AutoScribeIdentityProfile),
            natives);
    }
}
