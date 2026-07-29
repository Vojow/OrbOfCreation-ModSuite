#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileMeasurement
{
    internal ServiceCycleProfileMeasurement(
        in ServiceCycleProfileContext context,
        long startedAtRawTicks,
        long elapsedRawTicks,
        long allocatedBytes,
        in ServiceCycleProfileOperations operations)
    {
        if (!context.IsValid) throw new ArgumentException("A valid profile context is required.", nameof(context));
        if (elapsedRawTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedRawTicks));
        if (allocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
        Context = context;
        StartedAtRawTicks = startedAtRawTicks;
        ElapsedRawTicks = elapsedRawTicks;
        AllocatedBytes = allocatedBytes;
        Operations = operations;
    }

    internal ServiceCycleProfileContext Context { get; }
    internal long StartedAtRawTicks { get; }
    internal long ElapsedRawTicks { get; }
    internal long AllocatedBytes { get; }
    internal ServiceCycleProfileOperations Operations { get; }
}
#endif
