using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptServiceAdapterComposition
{
    private AutoConceptServiceAdapterComposition(
        IAutomataServiceDefinition<AutoConceptCycleState, AutoConceptCycleAction> definition,
        AutoConceptNativeAdapter natives)
    {
        Definition = definition;
        Natives = natives;
    }

    internal IAutomataServiceDefinition<AutoConceptCycleState, AutoConceptCycleAction> Definition { get; }
    internal AutoConceptNativeAdapter Natives { get; }

    internal static AutoConceptServiceAdapterComposition Create(
        AutoConceptFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var natives = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var actions = new AutoConceptCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily);
        return new AutoConceptServiceAdapterComposition(AutoConceptService.Define(actions), natives);
    }
}
