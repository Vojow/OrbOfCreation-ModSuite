#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal struct ServiceCycleProfileAggregateBucket
{
    internal bool Occupied;
    internal int GroupOrdinal;
    internal ServiceCycleProfileAggregateKey Key;
    internal long MinimumStartedAtRawTicks;
    internal long MaximumStartedAtRawTicks;
    internal ulong OccurrenceCount;
    internal ulong TotalElapsedRawTicks;
    internal long MinimumElapsedRawTicks;
    internal long MaximumElapsedRawTicks;
    internal ulong TotalAllocatedBytes;
    internal int SampleCount;

    internal void Initialize(
        int groupOrdinal,
        in ServiceCycleProfileAggregateKey key,
        in ServiceCycleProfileMeasurement measurement)
    {
        Occupied = true;
        GroupOrdinal = groupOrdinal;
        Key = key;
        MinimumStartedAtRawTicks = measurement.StartedAtRawTicks;
        MaximumStartedAtRawTicks = measurement.StartedAtRawTicks;
        OccurrenceCount = 1;
        TotalElapsedRawTicks = checked((ulong)measurement.ElapsedRawTicks);
        MinimumElapsedRawTicks = measurement.ElapsedRawTicks;
        MaximumElapsedRawTicks = measurement.ElapsedRawTicks;
        TotalAllocatedBytes = checked((ulong)measurement.AllocatedBytes);
    }

    internal bool TryAdd(in ServiceCycleProfileMeasurement measurement)
    {
        if (OccurrenceCount == ulong.MaxValue ||
            !TryAdd(TotalElapsedRawTicks, checked((ulong)measurement.ElapsedRawTicks), out var totalElapsed) ||
            !TryAdd(TotalAllocatedBytes, checked((ulong)measurement.AllocatedBytes), out var totalAllocated))
            return false;
        OccurrenceCount++;
        TotalElapsedRawTicks = totalElapsed;
        TotalAllocatedBytes = totalAllocated;
        MinimumStartedAtRawTicks = Math.Min(MinimumStartedAtRawTicks, measurement.StartedAtRawTicks);
        MaximumStartedAtRawTicks = Math.Max(MaximumStartedAtRawTicks, measurement.StartedAtRawTicks);
        MinimumElapsedRawTicks = Math.Min(MinimumElapsedRawTicks, measurement.ElapsedRawTicks);
        MaximumElapsedRawTicks = Math.Max(MaximumElapsedRawTicks, measurement.ElapsedRawTicks);
        return true;
    }

    internal ServiceCycleProfileRecord ToRecord()
    {
        var operations = Key.Operations;
        return ServiceCycleProfileRecord.Aggregate(
            Key.StageCode,
            Key.ServiceOrdinal,
            Key.Lifecycle,
            MinimumStartedAtRawTicks,
            MaximumStartedAtRawTicks,
            OccurrenceCount,
            TotalElapsedRawTicks,
            MinimumElapsedRawTicks,
            MaximumElapsedRawTicks,
            TotalAllocatedBytes,
            Key.Temperature,
            in operations);
    }

    private static bool TryAdd(ulong left, ulong right, out ulong value)
    {
        if (ulong.MaxValue - left < right)
        {
            value = 0;
            return false;
        }
        value = left + right;
        return true;
    }
}
#endif
