using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoConceptFeatureDependencies
{
    public AutoConceptFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutomataFeatureStatusReporter? featureStatus = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        FeatureStatus = featureStatus;
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<bool> OwnsActionFamily { get; }
    public AutomataFeatureStatusReporter? FeatureStatus { get; }
}
