using OrbModding.Common.Runtime;
using System;
using Xunit;

namespace OrbModding.Tests.Runtime;

public sealed partial class BoundedRuntimePrimitiveTests
{
    [Fact]
    public void MonotonicTimeUsesTimeSpanTicksAndRejectsNegativeOrOverflowingArithmetic()
    {
        Assert.Equal(TimeSpan.TicksPerSecond, MonotonicDuration.TicksPerSecond);
        Assert.Equal(100, MonotonicDuration.NanosecondsPerTick);
        var duration = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(2));
        Assert.Equal(TimeSpan.TicksPerMillisecond * 2, duration.Ticks);
        Assert.Equal(TimeSpan.FromMilliseconds(2), duration.ToTimeSpan());

        var start = new MonotonicTimestamp(TimeSpan.TicksPerSecond);
        var end = start + duration;
        Assert.Equal(duration, end - start);
        Assert.Equal(start, end - duration);

        var clock = new VirtualMonotonicClock(start);
        clock.Advance(duration);
        Assert.Equal(end, clock.Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MonotonicDuration(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonotonicDuration.FromTimeSpan(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonotonicTimestamp(0) - new MonotonicDuration(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonotonicTimestamp(0) - new MonotonicTimestamp(1));
        Assert.Throws<OverflowException>(() => new MonotonicTimestamp(long.MaxValue) + new MonotonicDuration(1));
        Assert.Throws<OverflowException>(() =>
            new VirtualMonotonicClock(new MonotonicTimestamp(long.MaxValue)).Advance(new MonotonicDuration(1)));
    }

}
