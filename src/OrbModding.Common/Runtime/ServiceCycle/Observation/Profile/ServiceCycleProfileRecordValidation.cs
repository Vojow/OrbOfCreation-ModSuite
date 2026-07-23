#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal static class ServiceCycleProfileRecordValidation
{
    internal static void Validate(in ServiceCycleProfileRecord record)
    {
        if (record.Kind is < ServiceCycleProfileRecordKind.Aggregate or > ServiceCycleProfileRecordKind.Sample ||
            record.StageCode <= 0 || record.ServiceOrdinal < 0 || record.OccurrenceCount == 0 ||
            record.LastStartedAtRawTicks < record.FirstStartedAtRawTicks ||
            record.MinimumElapsedRawTicks < 0 ||
            record.MaximumElapsedRawTicks < record.MinimumElapsedRawTicks ||
            record.Temperature is < ServiceCycleProfileTemperature.ColdProcess or
                > ServiceCycleProfileTemperature.Warm)
            throw new ArgumentException("The service-cycle profile record is invalid.", nameof(record));
        var minimumElapsed = checked((ulong)record.MinimumElapsedRawTicks);
        var maximumElapsed = checked((ulong)record.MaximumElapsedRawTicks);
        if (!TryMultiply(minimumElapsed, record.OccurrenceCount - 1, out var remainingMinimum) ||
            !TryAdd(maximumElapsed, remainingMinimum, out var lowerElapsed))
            throw new ArgumentException("The profile elapsed aggregate cannot be represented.", nameof(record));
        var upperElapsed = SaturatingMultiply(
            maximumElapsed,
            record.OccurrenceCount);
        if (record.TotalElapsedRawTicks < lowerElapsed || record.TotalElapsedRawTicks > upperElapsed)
            throw new ArgumentException("The profile elapsed aggregate is impossible.", nameof(record));
        if (record.Kind == ServiceCycleProfileRecordKind.Sample &&
            (record.OccurrenceCount != 1 || record.FirstStartedAtRawTicks != record.LastStartedAtRawTicks ||
                record.TotalElapsedRawTicks != checked((ulong)record.MinimumElapsedRawTicks) ||
                record.MinimumElapsedRawTicks != record.MaximumElapsedRawTicks) ||
            record.Kind == ServiceCycleProfileRecordKind.Aggregate &&
                (record.Cycle != 0 || record.Frame != 0))
            throw new ArgumentException("The profile record kind and payload disagree.", nameof(record));
    }

    private static ulong SaturatingMultiply(ulong left, ulong right) =>
        left != 0 && right > ulong.MaxValue / left ? ulong.MaxValue : left * right;

    private static bool TryMultiply(ulong left, ulong right, out ulong value)
    {
        if (left != 0 && right > ulong.MaxValue / left)
        {
            value = 0;
            return false;
        }
        value = left * right;
        return true;
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
