using OrbModding.Common;
using OrbModding.Common.Runtime;
using System;
using System.Reflection;
using Xunit;

namespace OrbModding.Tests.Runtime;

public sealed partial class PilotRuntimeFoundationTests
{
    [Fact]
    public void StopwatchConversionNormalizesSyntheticFrequenciesAndChecksOverflow()
    {
        Assert.Equal(
            new MonotonicDuration(TimeSpan.TicksPerSecond),
            StopwatchMonotonicClock.ConvertElapsedTicks(3, 3));
        Assert.Equal(
            new MonotonicDuration(3_333_333),
            StopwatchMonotonicClock.ConvertElapsedTicks(1, 3));
        Assert.Equal(
            new MonotonicDuration(TimeSpan.TicksPerSecond),
            StopwatchMonotonicClock.ConvertElapsedTicks(long.MaxValue, long.MaxValue));
        Assert.Equal(
            new MonotonicDuration(5_000_000),
            StopwatchMonotonicClock.ConvertElapsedTicks(1, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StopwatchMonotonicClock.ConvertElapsedTicks(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StopwatchMonotonicClock.ConvertElapsedTicks(1, 0));
        Assert.Throws<OverflowException>(() =>
            StopwatchMonotonicClock.ConvertElapsedTicks(long.MaxValue, 1));
    }

    [Fact]
    public void StopwatchClockUsesCapturedArbitraryOriginAndCannotMoveBackwards()
    {
        long raw = 80_000;
        var clock = new StopwatchMonotonicClock(() => raw, frequency: 8_000);
        Assert.Equal(default, clock.Now);

        raw = 84_000;
        var middle = clock.Now;
        raw = 88_000;
        var end = clock.Now;

        Assert.Equal(new MonotonicTimestamp(5_000_000), middle);
        Assert.Equal(new MonotonicTimestamp(10_000_000), end);
        Assert.True(end >= middle);

        raw = 79_999;
        Assert.Throws<InvalidOperationException>(() => _ = clock.Now);
    }

}
