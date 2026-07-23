using System;

namespace OrbAutomata;

/// <summary>
/// The single concrete production ordering seam for Automata runtime services.
/// Factories keep construction feature-owned while registration order remains
/// explicit, deterministic, and independently regression-testable.
/// </summary>
internal static class AutomataProductionComposition
{
    internal const int CoreServiceCount = 4;
    internal const int FullServiceCount = 5;

    public static void Register(
        AutomataServiceRegistry registry,
        Func<IAutomataService?> tryCreateAutoHarvest,
        Func<IAutomataService> createAutoBuy,
        Func<IAutomataService> createAutoCast,
        Func<IAutomataService> createAutoConcept,
        Func<IAutomataService> createSpellLevel)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (registry.Count != 0)
            throw new InvalidOperationException("Production services require an empty ordered registry.");

        if (tryCreateAutoHarvest is null) throw new ArgumentNullException(nameof(tryCreateAutoHarvest));
        var autoHarvest = tryCreateAutoHarvest();
        if (autoHarvest is not null) registry.Register(autoHarvest);
        RegisterCreated(registry, createAutoBuy, nameof(createAutoBuy));
        RegisterCreated(registry, createAutoCast, nameof(createAutoCast));
        RegisterCreated(registry, createAutoConcept, nameof(createAutoConcept));
        RegisterCreated(registry, createSpellLevel, nameof(createSpellLevel));
    }

    private static void RegisterCreated(
        AutomataServiceRegistry registry,
        Func<IAutomataService> factory,
        string parameterName)
    {
        if (factory is null) throw new ArgumentNullException(parameterName);
        var service = factory() ??
            throw new InvalidOperationException(parameterName + " returned no runtime service.");
        registry.Register(service);
    }
}
