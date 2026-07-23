#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal readonly struct ServiceCycleProfileRecord
{
    internal ServiceCycleProfileRecord(
        ServiceCycleProfileRecordKind kind,
        int stageCode,
        int serviceOrdinal,
        ulong lifecycle,
        ulong cycle,
        ulong frame,
        long firstStartedAtRawTicks,
        long lastStartedAtRawTicks,
        ulong occurrenceCount,
        ulong totalElapsedRawTicks,
        long minimumElapsedRawTicks,
        long maximumElapsedRawTicks,
        ulong totalAllocatedBytes,
        ServiceCycleProfileTemperature temperature,
        in ServiceCycleProfileOperations operations)
    {
        Kind = kind;
        StageCode = stageCode;
        ServiceOrdinal = serviceOrdinal;
        Lifecycle = lifecycle;
        Cycle = cycle;
        Frame = frame;
        FirstStartedAtRawTicks = firstStartedAtRawTicks;
        LastStartedAtRawTicks = lastStartedAtRawTicks;
        OccurrenceCount = occurrenceCount;
        TotalElapsedRawTicks = totalElapsedRawTicks;
        MinimumElapsedRawTicks = minimumElapsedRawTicks;
        MaximumElapsedRawTicks = maximumElapsedRawTicks;
        TotalAllocatedBytes = totalAllocatedBytes;
        Temperature = temperature;
        Operations = operations;
        ServiceCycleProfileRecordValidation.Validate(in this);
    }

    internal ServiceCycleProfileRecordKind Kind { get; }
    internal int StageCode { get; }
    internal int ServiceOrdinal { get; }
    internal ulong Lifecycle { get; }
    internal ulong Cycle { get; }
    internal ulong Frame { get; }
    internal long FirstStartedAtRawTicks { get; }
    internal long LastStartedAtRawTicks { get; }
    internal ulong OccurrenceCount { get; }
    internal ulong TotalElapsedRawTicks { get; }
    internal long MinimumElapsedRawTicks { get; }
    internal long MaximumElapsedRawTicks { get; }
    internal ulong TotalAllocatedBytes { get; }
    internal ServiceCycleProfileTemperature Temperature { get; }
    internal ServiceCycleProfileOperations Operations { get; }

    internal static ServiceCycleProfileRecord Sample(
        int stageCode,
        int serviceOrdinal,
        ulong lifecycle,
        ulong cycle,
        ulong frame,
        long startedAtRawTicks,
        long elapsedRawTicks,
        long allocatedBytes,
        ServiceCycleProfileTemperature temperature,
        in ServiceCycleProfileOperations operations)
    {
        if (elapsedRawTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedRawTicks));
        if (allocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(allocatedBytes));
        return new ServiceCycleProfileRecord(
            ServiceCycleProfileRecordKind.Sample,
            stageCode,
            serviceOrdinal,
            lifecycle,
            cycle,
            frame,
            startedAtRawTicks,
            startedAtRawTicks,
            1,
            checked((ulong)elapsedRawTicks),
            elapsedRawTicks,
            elapsedRawTicks,
            checked((ulong)allocatedBytes),
            temperature,
            in operations);
    }

    internal static ServiceCycleProfileRecord Aggregate(
        int stageCode,
        int serviceOrdinal,
        ulong lifecycle,
        long firstStartedAtRawTicks,
        long lastStartedAtRawTicks,
        ulong occurrenceCount,
        ulong totalElapsedRawTicks,
        long minimumElapsedRawTicks,
        long maximumElapsedRawTicks,
        ulong totalAllocatedBytes,
        ServiceCycleProfileTemperature temperature,
        in ServiceCycleProfileOperations operations) =>
        new(
            ServiceCycleProfileRecordKind.Aggregate,
            stageCode,
            serviceOrdinal,
            lifecycle,
            cycle: 0,
            frame: 0,
            firstStartedAtRawTicks,
            lastStartedAtRawTicks,
            occurrenceCount,
            totalElapsedRawTicks,
            minimumElapsedRawTicks,
            maximumElapsedRawTicks,
            totalAllocatedBytes,
            temperature,
            in operations);
}
#endif
