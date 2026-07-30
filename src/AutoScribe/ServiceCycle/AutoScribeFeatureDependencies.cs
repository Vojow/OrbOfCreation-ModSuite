using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class AutoScribeFeatureDependencies
{
    internal AutoScribeFeatureDependencies(
        TypedRegistryResolver registry,
        AutoScribeIdentityProfile profile,
        Func<long> readEpoch,
        Func<bool> owns,
        Func<bool> canConsumeScrolls,
        Func<bool> capturePermit,
        AutomataFeatureStatusReporter featureStatus)
    {
        Registry = registry;
        Profile = profile;
        ReadEpoch = readEpoch;
        Owns = owns;
        CanConsumeScrolls = canConsumeScrolls;
        CapturePermit = capturePermit;
        FeatureStatus = featureStatus;
    }

    internal TypedRegistryResolver Registry { get; }
    internal AutoScribeIdentityProfile Profile { get; }
    internal Func<long> ReadEpoch { get; }
    internal Func<bool> Owns { get; }
    internal Func<bool> CanConsumeScrolls { get; }
    internal Func<bool> CapturePermit { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
}
