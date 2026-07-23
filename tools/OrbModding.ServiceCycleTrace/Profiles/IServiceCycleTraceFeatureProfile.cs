using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

namespace OrbModding.ServiceCycleTrace.Profiles;

internal interface IServiceCycleTraceFeatureProfile
{
    string DisplayName { get; }
    bool Includes(ServiceCycleReplayArtifactCycle cycle);
    string DescribeAction(ServiceCycleReplayArtifactCycle cycle);
}
