using System;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

namespace OrbModConfig;

internal enum PumpTimingGraphPhase
{
    Idle = 0,
    Capture = 1,
    Response = 2,
    Action = 3,
}

internal readonly struct PumpTimingGraphColumn
{
    internal PumpTimingGraphColumn(long durationTicks, PumpTimingGraphPhase phase)
    {
        DurationTicks = durationTicks;
        Phase = phase;
    }

    internal long DurationTicks { get; }
    internal PumpTimingGraphPhase Phase { get; }
}

internal static class PumpTimingGraphProjection
{
    internal static int Build(
        ReadOnlySpan<ServiceCyclePumpTimingSample> samples,
        Span<PumpTimingGraphColumn> columns)
    {
        if (samples.Length == 0 || columns.Length == 0) return 0;
        var written = Math.Min(samples.Length, columns.Length);
        for (var index = 0; index < written; index++)
        {
            var sample = samples[index];
            columns[index] = new PumpTimingGraphColumn(
                sample.TotalDuration.Ticks,
                Phase(in sample));
        }
        return written;
    }

    internal static float Height(long durationTicks, long maximumTicks)
    {
        if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (maximumTicks < 0) throw new ArgumentOutOfRangeException(nameof(maximumTicks));
        var scaleTicks = Math.Max(1, maximumTicks);
        return Math.Min(
            1f,
            Math.Max(0.01f, (float)(durationTicks / (double)scaleTicks)));
    }

    private static PumpTimingGraphPhase Phase(in ServiceCyclePumpTimingSample sample)
    {
        if (sample.ActionsAttempted > 0) return PumpTimingGraphPhase.Action;
        if (sample.ResponsesAcquired > 0) return PumpTimingGraphPhase.Response;
        if (sample.CapturesAttempted > 0) return PumpTimingGraphPhase.Capture;
        return PumpTimingGraphPhase.Idle;
    }

}
