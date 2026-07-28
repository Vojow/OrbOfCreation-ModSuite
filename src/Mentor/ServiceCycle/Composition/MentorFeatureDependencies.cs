using System;
using OrbAutomata;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal sealed class MentorFeatureDependencies
{
    internal MentorFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<MasteryExperienceDomain, bool> captureMutationPermit,
        AutomataFeatureStatusReporter? featureStatus = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ??
                             throw new ArgumentNullException(nameof(readLifecycleEpoch));
        CaptureMutationPermit = captureMutationPermit ??
                                throw new ArgumentNullException(nameof(captureMutationPermit));
        FeatureStatus = featureStatus;
    }

    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<MasteryExperienceDomain, bool> CaptureMutationPermit { get; }
    internal AutomataFeatureStatusReporter? FeatureStatus { get; }
}
