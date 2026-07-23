#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpEvidenceProfiler
{
    private readonly ServiceCycleProfileProbe _probe;

    internal SuiteFramePumpEvidenceProfiler(ServiceCycleProfileProbe probe) =>
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    internal ServiceCycleProfileStageScope BeginPump(
        ulong lifecycle,
        long frameIdentity) =>
        Begin(
            ServiceCycleProfileCommonStageCodes.OverallPump,
            serviceOrdinal: 0,
            lifecycle,
            cycle: 0,
            frameIdentity);

    internal ServiceCycleProfileStageScope Begin(
        int stageCode,
        int serviceOrdinal,
        ulong lifecycle,
        ulong cycle,
        long frameIdentity)
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal, frameIdentity);
        return coordinates.TryCreateContext(
                stageCode,
                lifecycle,
                cycle,
                ServiceCycleProfileTemperature.Warm,
                out var context)
            ? _probe.Begin(in context)
            : default;
    }
}
#endif
