using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Diagnostics;

public sealed class ServiceCyclePumpTimingRegistryTests
{
    [Fact]
    public void DefaultHistoryCoversSixtySecondsAtSixtyFramesPerSecond()
    {
        Assert.Equal(1_200, ServiceCyclePumpTimingRegistry.DefaultCapacity);
        Assert.Equal(
            ServiceCyclePumpTimingRegistry.DefaultCapacity,
            ServiceCyclePumpTimingRegistry.Shared.Capacity);
    }

    [Fact]
    public void BoundedHistoryCopiesNewestAcceptedFramesInTimeOrder()
    {
        var history = new ServiceCyclePumpTimingRegistry(3);
        history.Observe(Report(1, accepted: true));
        history.Observe(Report(2, accepted: false));
        history.Observe(Report(3, accepted: true));
        history.Observe(Report(4, accepted: true));
        history.Observe(Report(5, accepted: true));

        var samples = new ServiceCyclePumpTimingSample[2];
        var copy = history.CopyTo(samples);

        Assert.Equal(3, copy.AvailableCount);
        Assert.Equal(2, copy.WrittenCount);
        Assert.False(copy.IsComplete);
        Assert.Equal(4, copy.Revision);
        Assert.Equal(new long[] { 4, 5 }, Array.ConvertAll(samples, sample => sample.FrameIdentity));
        Assert.Equal(TimeSpan.FromMilliseconds(5).Ticks, samples[1].TotalDuration.Ticks);
    }

    private static SuiteFramePumpReport Report(long frame, bool accepted) => new(
        frame,
        accepted,
        startingOrdinal: 0,
        responsesAcquired: frame == 4 ? 1 : 0,
        actionsAttempted: frame == 5 ? 1 : 0,
        capturesAttempted: frame == 3 ? 1 : 0,
        emergencyBatchesRejected: 0,
        lifecyclePositionTransitions: 0,
        responseDuration: default,
        actionDuration: default,
        captureDuration: default,
        totalDuration: MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(frame)));
}
