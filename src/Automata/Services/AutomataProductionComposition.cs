using System;

namespace OrbAutomata;

/// <summary>
/// The single concrete production ordering seam for Automata runtime services.
/// Factories keep construction feature-owned while registration order remains
/// explicit, deterministic, and independently regression-testable.
/// </summary>
internal static class AutomataProductionComposition
{
    internal const int CoreServiceCount = 0;
    internal const int FullServiceCount = 1;

    public static void Register(
        AutomataServiceRegistry registry,
        Func<IAutomataService?> tryCreateServiceCycle)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (registry.Count != 0)
            throw new InvalidOperationException("Production services require an empty ordered registry.");

        if (tryCreateServiceCycle is null)
            throw new ArgumentNullException(nameof(tryCreateServiceCycle));
        var serviceCycle = tryCreateServiceCycle();
        if (serviceCycle is not null) registry.Register(serviceCycle);
    }
}
