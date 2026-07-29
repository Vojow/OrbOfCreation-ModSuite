using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutoHarvestNativeLifecycle
{
    public static bool Matches(long nativeLifecycle, LifecycleGeneration plannedLifecycle) =>
        nativeLifecycle > 0 &&
        (ulong)nativeLifecycle == plannedLifecycle.Value;
}
