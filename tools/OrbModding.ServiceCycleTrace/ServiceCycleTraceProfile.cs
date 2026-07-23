using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.ServiceCycleTrace.Profiles;

namespace OrbModding.ServiceCycleTrace;

public enum ServiceCycleTraceProfile
{
    Generic,
    AutoHarvest,
}

internal static class ServiceCycleTraceProfiles
{
    internal static IServiceCycleTraceFeatureProfile? BindSelected(
        ServiceCycleTraceProfile profile,
        ServiceCycleReplayArtifactDocument artifact) => profile switch
    {
        ServiceCycleTraceProfile.Generic => null,
        ServiceCycleTraceProfile.AutoHarvest => AutoHarvestTraceProfile.BindAssertedFeature(artifact),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
