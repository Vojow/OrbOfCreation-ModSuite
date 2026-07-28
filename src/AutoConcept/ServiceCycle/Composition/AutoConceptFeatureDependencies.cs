using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptFeatureDependencies
{
    public AutoConceptFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutomataFeatureStatusReporter featureStatus)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<bool> OwnsActionFamily { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }
}
