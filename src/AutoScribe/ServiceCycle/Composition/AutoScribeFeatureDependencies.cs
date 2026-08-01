using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoScribeFeatureDependencies
{
    internal AutoScribeFeatureDependencies(
        TypedRegistryResolver registryResolver,
        AutoScribeIdentityProfile profile,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        AutomataFeatureStatusReporter featureStatus,
        ConsumableMutationGate? mutationGate = null,
        Func<long>? readFrameIdentity = null)
    {
        RegistryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ReadLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        ReadOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        FeatureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
        MutationGate = mutationGate ?? new ConsumableMutationGate();
        ReadFrameIdentity = readFrameIdentity ?? (() => 0);
    }

    internal TypedRegistryResolver RegistryResolver { get; }
    internal AutoScribeIdentityProfile Profile { get; }
    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<bool> OwnsActionFamily { get; }
    internal Func<bool> TryCaptureMutationPermit { get; }
    internal Func<string> ReadOwnershipFailure { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
    internal ConsumableMutationGate MutationGate { get; }
    internal Func<long> ReadFrameIdentity { get; }
}
