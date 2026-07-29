using System;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class PumpTimingGraphProjectionTests
{
    [Fact]
    public void ContinuousProjectionPreservesEverySelectedFrame()
    {
        var samples = new[]
        {
            Sample(1, 1),
            Sample(2, 2),
            Sample(3, 3, captures: 1),
            Sample(4, 4),
            Sample(5, 5, responses: 1),
            Sample(6, 6, actions: 1),
        };
        var columns = new PumpTimingGraphColumn[6];

        var written = PumpTimingGraphProjection.Build(samples, columns);

        Assert.Equal(6, written);
        Assert.Equal(TimeSpan.FromMilliseconds(1).Ticks, columns[0].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Idle, columns[0].Phase);
        Assert.Equal(TimeSpan.FromMilliseconds(3).Ticks, columns[2].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Capture, columns[2].Phase);
        Assert.Equal(TimeSpan.FromMilliseconds(5).Ticks, columns[4].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Response, columns[4].Phase);
        Assert.Equal(TimeSpan.FromMilliseconds(6).Ticks, columns[5].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Action, columns[5].Phase);
    }

    [Fact]
    public void ShortHistoryRetainsOneColumnPerFrame()
    {
        var samples = new[] { Sample(1, 1), Sample(2, 7, responses: 1) };
        var columns = new PumpTimingGraphColumn[180];

        var written = PumpTimingGraphProjection.Build(samples, columns);

        Assert.Equal(2, written);
        Assert.Equal(TimeSpan.FromMilliseconds(1).Ticks, columns[0].DurationTicks);
        Assert.Equal(TimeSpan.FromMilliseconds(7).Ticks, columns[1].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Response, columns[1].Phase);
    }

    [Fact]
    public void FullHistoryRetainsTwelveHundredDistinctFrames()
    {
        var samples = new ServiceCyclePumpTimingSample[1_200];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Sample(
                index + 1,
                milliseconds: index + 1,
                captures: index is 598 or 599 ? 1 : 0);
        }
        var columns = new PumpTimingGraphColumn[samples.Length];

        var written = PumpTimingGraphProjection.Build(samples, columns);

        Assert.Equal(1_200, written);
        Assert.Equal(TimeSpan.FromMilliseconds(599).Ticks, columns[598].DurationTicks);
        Assert.Equal(TimeSpan.FromMilliseconds(600).Ticks, columns[599].DurationTicks);
        Assert.Equal(PumpTimingGraphPhase.Capture, columns[598].Phase);
        Assert.Equal(PumpTimingGraphPhase.Capture, columns[599].Phase);
    }

    [Fact]
    public void OneWarmupFrameDoesNotFlattenTheRestOfTheWindow()
    {
        var sorted = new long[100];
        for (var index = 0; index < sorted.Length - 1; index++)
            sorted[index] = TimeSpan.FromMilliseconds(1).Ticks;
        sorted[^1] = TimeSpan.FromMilliseconds(244).Ticks;

        var scale = PumpTimingGraphProjection.ScaleTicks(sorted);

        Assert.Equal(TimeSpan.FromMilliseconds(1).Ticks, scale);
        Assert.Equal(1f, PumpTimingGraphProjection.Height(sorted[0], scale));
        Assert.Equal(1f, PumpTimingGraphProjection.Height(sorted[^1], scale));
    }

    [Fact]
    public void ScaleTracksTheWindowWhenNothingIsAnOutlier()
    {
        var sorted = new long[100];
        for (var index = 0; index < sorted.Length; index++)
            sorted[index] = TimeSpan.FromMilliseconds(index + 1).Ticks;

        var scale = PumpTimingGraphProjection.ScaleTicks(sorted);

        Assert.Equal(TimeSpan.FromMilliseconds(99).Ticks, scale);
        Assert.InRange(PumpTimingGraphProjection.Height(sorted[49], scale), 0.5f, 0.51f);
    }

    [Fact]
    public void AnIdleFrameDrawsNothingRatherThanAStub()
    {
        Assert.Equal(0f, PumpTimingGraphProjection.Height(0, TimeSpan.FromMilliseconds(1).Ticks));
        Assert.Equal(1, PumpTimingGraphProjection.ScaleTicks(ReadOnlySpan<long>.Empty));
    }

    private static ServiceCyclePumpTimingSample Sample(
        long frame,
        double milliseconds,
        int responses = 0,
        int actions = 0,
        int captures = 0)
    {
        var report = new SuiteFramePumpReport(
            frame,
            accepted: true,
            startingOrdinal: 0,
            responses,
            actions,
            captures,
            cyclesStarted: captures,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecyclePositionTransitions: 0,
            responseDuration: default,
            actionDuration: default,
            captureDuration: default,
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)));
        return new ServiceCyclePumpTimingSample(in report);
    }
}
