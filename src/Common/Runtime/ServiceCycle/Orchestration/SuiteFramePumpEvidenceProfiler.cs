#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal sealed class SuiteFramePumpEvidenceProfiler
{
    private readonly ServiceCycleProfileProbe _probe;

    internal SuiteFramePumpEvidenceProfiler(ServiceCycleProfileProbe probe) =>
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    /// <summary>
    /// Opens a span that belongs to the frame rather than to any one service: the whole pump, and each
    /// phase inside it.
    /// </summary>
    internal ServiceCycleProfileStageScope BeginFrame(
        ServiceCycleProfileSpan span,
        ulong lifecycle,
        long frameIdentity) =>
        Begin(
            span,
            serviceOrdinal: 0,
            lifecycle,
            cycle: 0,
            frameIdentity);

    internal ServiceCycleProfileStageScope Begin(
        ServiceCycleProfileSpan span,
        int serviceOrdinal,
        ulong lifecycle,
        ulong cycle,
        long frameIdentity)
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal, frameIdentity);
        return coordinates.TryCreateContext(
                span,
                lifecycle,
                cycle,
                ServiceCycleProfileTemperature.Warm,
                out var context)
            ? _probe.Begin(in context)
            : default;
    }
}
#endif
