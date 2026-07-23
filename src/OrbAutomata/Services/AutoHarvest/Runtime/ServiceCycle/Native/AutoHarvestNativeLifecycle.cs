using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutoHarvestNativeLifecycle
{
    public static bool Matches(long nativeLifecycle, LifecycleGeneration plannedLifecycle) =>
        nativeLifecycle > 0 &&
        (ulong)nativeLifecycle == plannedLifecycle.Value;

    public static bool Matches(
        in AutoHarvestResolvedPairSet pairs,
        LifecycleGeneration plannedLifecycle)
    {
        if (pairs.Fruit.Succeeded &&
            !Matches(pairs.Fruit.Pair.LifecycleGeneration, plannedLifecycle))
            return false;
        return !pairs.Treasure.Succeeded ||
               Matches(pairs.Treasure.Pair.LifecycleGeneration, plannedLifecycle);
    }
}
