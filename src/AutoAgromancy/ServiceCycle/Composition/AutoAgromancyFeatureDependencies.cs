using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoAgromancyFeatureDependencies
{
    internal AutoAgromancyFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<bool> tryCaptureMutationPermit,
        Func<SuiteRuntimeConfiguration> readConfiguration,
        Func<ConfigGeneration> readConfigurationGeneration,
        AutomataFeatureStatusReporter featureStatus,
        Func<GameWorldCollector>? createLiveCollector = null)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        TryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        ReadConfiguration = readConfiguration ??
            throw new ArgumentNullException(nameof(readConfiguration));
        ReadConfigurationGeneration = readConfigurationGeneration ??
            throw new ArgumentNullException(nameof(readConfigurationGeneration));
        FeatureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));
        CreateLiveCollector = createLiveCollector ?? (() => new GameWorldCollector());
    }

    internal Func<long> ReadLifecycleEpoch { get; }
    internal Func<bool> OwnsActionFamily { get; }
    internal Func<bool> TryCaptureMutationPermit { get; }
    internal Func<SuiteRuntimeConfiguration> ReadConfiguration { get; }
    internal Func<ConfigGeneration> ReadConfigurationGeneration { get; }
    internal AutomataFeatureStatusReporter FeatureStatus { get; }
    internal Func<GameWorldCollector> CreateLiveCollector { get; }
}
