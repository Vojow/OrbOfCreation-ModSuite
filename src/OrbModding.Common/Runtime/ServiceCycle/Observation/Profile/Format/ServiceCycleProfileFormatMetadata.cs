#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal static class ServiceCycleProfileFormatMetadata
{
    internal const uint TraceActiveFlag = 1;
    internal const uint AllocationAvailableFlag = 2;
    internal const uint KnownFlags = TraceActiveFlag | AllocationAvailableFlag;

    internal static uint Flags(in ServiceCycleProfileCalibration calibration) =>
        (calibration.TraceActive ? TraceActiveFlag : 0) |
        (calibration.AllocationAvailable ? AllocationAvailableFlag : 0);

    internal static ServiceCycleProfileCalibration Calibration(
        uint flags,
        long timestampFrequency,
        long rawTimestamp,
        long monotonicTimestampTicks,
        System.Guid buildId)
    {
        if ((flags & ~KnownFlags) != 0)
            throw new System.FormatException("Invalid service-cycle profile flags.");
        return new ServiceCycleProfileCalibration(
            timestampFrequency,
            rawTimestamp,
            monotonicTimestampTicks,
            buildId,
            (flags & TraceActiveFlag) != 0,
            (flags & AllocationAvailableFlag) != 0);
    }
}
#endif
