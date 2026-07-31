using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoScribeFeatureDependencies
{
    internal AutoScribeFeatureDependencies(
        TypedRegistryResolver registryResolver,
        AutoScribeIdentityProfile profile,
        Func<long> readLifecycleEpoch,
        Func<long> readFrameIdentity,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        AutomataFeatureStatusReporter featureStatus,
        ConsumableMutationPublicationGapCoordinator publicationGap)
    {
        RegistryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ReadLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        ReadFrameIdentity = readFrameIdentity ??
            throw new ArgumentNullException(nameof(readFrameIdentity));
        OwnsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        ReadOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        FeatureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
        PublicationGap = publicationGap ??
            throw new ArgumentNullException(nameof(publicationGap));
    }

    internal TypedRegistryResolver RegistryResolver { get; }
    internal AutoScribeIdentityProfile Profile { get; }
    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<long> ReadFrameIdentity { get; }
    internal Func<bool> OwnsActionFamily { get; }
    internal Func<bool> TryCaptureMutationPermit { get; }
    internal Func<string> ReadOwnershipFailure { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
    internal ConsumableMutationPublicationGapCoordinator PublicationGap { get; }
}
