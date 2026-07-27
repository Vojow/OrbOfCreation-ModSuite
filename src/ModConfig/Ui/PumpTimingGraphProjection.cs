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

    /// <summary>
    /// The tick value the plot draws as full height, from the already-sorted window.
    /// </summary>
    /// <remarks>
    /// The ninety-ninth percentile rather than the maximum. One warm-up frame costs two hundred
    /// milliseconds against a steady frame of a fraction of one, and the ring holds twenty seconds,
    /// so scaling to the maximum flattens every ordinary frame to nothing for the whole time that
    /// one sample is retained. A percentile absorbs the same way for a scene load, a save, or a
    /// collection — none of which a "first N frames" rule would catch — and the frames above it are
    /// still drawn, clipped and marked, with the true maximum in the summary line.
    /// </remarks>
    internal static long ScaleTicks(ReadOnlySpan<long> sortedTicks)
    {
        if (sortedTicks.Length == 0) return 1;
        var index = Math.Min(
            sortedTicks.Length - 1,
            Math.Max(0, (int)Math.Ceiling(sortedTicks.Length * 0.99) - 1));
        return Math.Max(1, sortedTicks[index]);
    }

    internal static float Height(long durationTicks, long scaleTicks)
    {
        if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (scaleTicks < 0) throw new ArgumentOutOfRangeException(nameof(scaleTicks));
        return Math.Min(1f, (float)(durationTicks / (double)Math.Max(1, scaleTicks)));
    }

    private static PumpTimingGraphPhase Phase(in ServiceCyclePumpTimingSample sample)
    {
        if (sample.ActionsAttempted > 0) return PumpTimingGraphPhase.Action;
        if (sample.ResponsesAcquired > 0) return PumpTimingGraphPhase.Response;
        if (sample.CapturesAttempted > 0) return PumpTimingGraphPhase.Capture;
        return PumpTimingGraphPhase.Idle;
    }

}
