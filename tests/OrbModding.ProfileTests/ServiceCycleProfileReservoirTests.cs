using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileReservoirTests
{
    [Fact]
    public void EqualStreamsProduceEqualBoundedReservoirs()
    {
        var first = new ServiceCycleProfileAggregator(2, 4, allocationAvailable: true);
        var second = new ServiceCycleProfileAggregator(2, 4, allocationAvailable: true);
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var operations = ServiceCycleProfileAggregatorTests.Operations(listEntries: 1);

        for (var index = 1; index <= 100; index++)
        {
            var measurement = Measurement(in context, index, in operations);
            ServiceCycleProfileAggregatorTests.AssertAccepted(first.Record(in measurement));
            ServiceCycleProfileAggregatorTests.AssertAccepted(second.Record(in measurement));
        }
        first.Seal();
        second.Seal();

        Assert.Equal(4, first.GetSampleCount(0));
        Assert.Equal(first.GetSampleCount(0), second.GetSampleCount(0));
        AssertSamplesEqual(first, 0, second, 0);
        var retainedLaterObservation = false;
        for (var sampleOrdinal = 0; sampleOrdinal < first.GetSampleCount(0); sampleOrdinal++)
            retainedLaterObservation |= first.GetSample(0, sampleOrdinal).Cycle > 4;
        Assert.True(retainedLaterObservation);
    }

    [Fact]
    public void OtherGroupsDoNotChangeAGroupReservoir()
    {
        var isolated = new ServiceCycleProfileAggregator(2, 4, allocationAvailable: true);
        var interleaved = new ServiceCycleProfileAggregator(2, 4, allocationAvailable: true);
        var target = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var other = ServiceCycleProfileAggregatorTests.Context(stage: 2, lifecycle: 1);
        var operations = ServiceCycleProfileAggregatorTests.Operations(listEntries: 1);

        for (var index = 1; index <= 100; index++)
        {
            var targetMeasurement = Measurement(in target, index, in operations);
            ServiceCycleProfileAggregatorTests.AssertAccepted(isolated.Record(in targetMeasurement));
            ServiceCycleProfileAggregatorTests.AssertAccepted(interleaved.Record(in targetMeasurement));
            var otherMeasurement = Measurement(in other, index, in operations);
            ServiceCycleProfileAggregatorTests.AssertAccepted(interleaved.Record(in otherMeasurement));
        }
        isolated.Seal();
        interleaved.Seal();

        AssertSamplesEqual(isolated, 0, interleaved, 0);
    }

    private static ServiceCycleProfileMeasurement Measurement(
        in ServiceCycleProfileContext context,
        int index,
        in ServiceCycleProfileOperations operations)
    {
        var varyingContext = new ServiceCycleProfileContext(
            context.StageCode,
            context.ServiceOrdinal,
            context.Lifecycle,
            checked((ulong)index),
            checked((ulong)(index * 2)),
            context.Temperature);
        return ServiceCycleProfileAggregatorTests.Measurement(
            in varyingContext,
            startedAtRawTicks: index * 3,
            elapsedRawTicks: index,
            allocatedBytes: index * 5,
            in operations);
    }

    private static void AssertSamplesEqual(
        ServiceCycleProfileAggregator first,
        int firstGroup,
        ServiceCycleProfileAggregator second,
        int secondGroup)
    {
        var count = first.GetSampleCount(firstGroup);
        Assert.Equal(count, second.GetSampleCount(secondGroup));
        for (var sampleOrdinal = 0; sampleOrdinal < count; sampleOrdinal++)
        {
            var left = first.GetSample(firstGroup, sampleOrdinal);
            var right = second.GetSample(secondGroup, sampleOrdinal);
            Assert.Equal(left.StageCode, right.StageCode);
            Assert.Equal(left.ServiceOrdinal, right.ServiceOrdinal);
            Assert.Equal(left.Lifecycle, right.Lifecycle);
            Assert.Equal(left.Cycle, right.Cycle);
            Assert.Equal(left.Frame, right.Frame);
            Assert.Equal(left.FirstStartedAtRawTicks, right.FirstStartedAtRawTicks);
            Assert.Equal(left.TotalElapsedRawTicks, right.TotalElapsedRawTicks);
            Assert.Equal(left.TotalAllocatedBytes, right.TotalAllocatedBytes);
            Assert.Equal(left.Temperature, right.Temperature);
            Assert.Equal(left.Operations, right.Operations);
        }
    }
}
