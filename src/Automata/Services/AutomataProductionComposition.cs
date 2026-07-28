using System;

namespace OrbAutomata;

/// <summary>
/// The single concrete production ordering seam for Automata runtime services.
/// Factories keep construction feature-owned while registration order remains
/// explicit, deterministic, and independently regression-testable.
/// </summary>
internal static class AutomataProductionComposition
{
    internal const int CoreServiceCount = 1;
    internal const int FullServiceCount = 2;

    public static void Register(
        AutomataServiceRegistry registry,
        Func<IAutomataService?> tryCreateAutoHarvest,
        Func<IAutomataService> createAutoConcept)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (registry.Count != 0)
            throw new InvalidOperationException("Production services require an empty ordered registry.");

        if (tryCreateAutoHarvest is null) throw new ArgumentNullException(nameof(tryCreateAutoHarvest));
        var autoHarvest = tryCreateAutoHarvest();
        if (autoHarvest is not null) registry.Register(autoHarvest);
        RegisterCreated(registry, createAutoConcept, nameof(createAutoConcept));
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
