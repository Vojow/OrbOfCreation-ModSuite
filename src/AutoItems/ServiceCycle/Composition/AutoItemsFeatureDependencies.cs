using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoItemsFeatureDependencies
{
    internal AutoItemsFeatureDependencies(
        TypedRegistryResolver registryResolver,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        AutomataFeatureStatusReporter featureStatus)
    {
        RegistryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        ReadLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        FeatureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
    }

    internal TypedRegistryResolver RegistryResolver { get; }
    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<bool> OwnsActionFamily { get; }
    internal Func<bool> TryCaptureMutationPermit { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
}
