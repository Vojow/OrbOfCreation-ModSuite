using System;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutoHarvestBindingCoherence
{
    public static bool IsCurrent(
        TypedRegistryResolver resolver,
        AutoHarvestSharedBinding shared,
        AutoHarvestPairBinding? fruit,
        AutoHarvestPairBinding? treasure)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (shared is null) throw new ArgumentNullException(nameof(shared));

        return resolver.IsCurrent(shared.ActiveResolution) &&
               resolver.IsCurrent(shared.ScalingResolution) &&
               IsCurrent(fruit, shared.LifecycleGeneration, resolver) &&
               IsCurrent(treasure, shared.LifecycleGeneration, resolver);
    }

    public static bool IsCurrent(
        TypedRegistryResolver resolver,
        AutoHarvestSharedBinding shared,
        AutoHarvestPairBinding binding)
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        if (shared is null) throw new ArgumentNullException(nameof(shared));
        if (binding is null) throw new ArgumentNullException(nameof(binding));
        return IsCurrent(binding, shared.LifecycleGeneration, resolver);
    }

    private static bool IsCurrent(
        AutoHarvestPairBinding? binding,
        long lifecycle,
        TypedRegistryResolver resolver) =>
        binding is null ||
        binding.PlotResolution.LifecycleGeneration == lifecycle &&
        binding.ActionResolution.LifecycleGeneration == lifecycle &&
        binding.RewardResolution.LifecycleGeneration == lifecycle &&
        resolver.IsCurrent(binding.PlotResolution) &&
        resolver.IsCurrent(binding.ActionResolution) &&
        resolver.IsCurrent(binding.RewardResolution);
}
