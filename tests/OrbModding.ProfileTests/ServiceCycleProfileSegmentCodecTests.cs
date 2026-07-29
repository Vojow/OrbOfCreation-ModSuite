using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using Xunit;
using static OrbModding.ProfileTests.ServiceCycleProfileTestData;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileSegmentCodecTests
{
    [Fact]
    public void SegmentRoundTripsCalibrationEnvelopeAndRecords()
    {
        var calibration = Calibration(traceActive: true);
        var records = new[] { Record(startedAt: 320), Record(stage: 2, startedAt: 280) };
        var bytes = new byte[ServiceCycleProfileSegmentCodec.GetEncodedLength(records.Length)];

        ServiceCycleProfileSegmentCodec.Encode(
            new ServiceCycleProfileSessionId(11), 3, 9, in calibration, records, bytes);
        var actual = ServiceCycleProfileSegmentCodec.Decode(bytes);

        Assert.Equal((ulong)11, actual.Session.Value);
        Assert.Equal((ulong)3, actual.Ordinal);
        Assert.Equal((ulong)9, actual.FirstRecordSequence);
        Assert.True(actual.Calibration.TraceActive);
        Assert.True(actual.Calibration.AllocationAvailable);
        Assert.Equal(BuildId, actual.Calibration.BuildId);
        Assert.Equal(2, actual.Records.Length);
        Assert.Equal(280, actual.Records[1].FirstStartedAtRawTicks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(112)]
    [InlineData(-5)]
    public void CorruptEnvelopeFooterOrChecksumFailsClosed(int offset)
    {
        var calibration = Calibration();
        var record = Record();
        var bytes = new byte[ServiceCycleProfileSegmentCodec.GetEncodedLength(1)];
        ServiceCycleProfileSegmentCodec.Encode(
            new ServiceCycleProfileSessionId(1), 0, 1, in calibration, new[] { record }, bytes);
        var mutationOffset = offset < 0 ? bytes.Length + offset : offset;
        bytes[mutationOffset] ^= 1;

        Assert.Throws<FormatException>(() => ServiceCycleProfileSegmentCodec.Decode(bytes));
    }

    [Fact]
    public void AllocationUnavailableCannotMasqueradeAsZeroMeasuredOverhead()
    {
        var calibration = Calibration(allocationAvailable: false);
        var measured = Record(allocatedBytes: 1);
        var destination = new byte[ServiceCycleProfileSegmentCodec.GetEncodedLength(1)];

        Assert.Throws<ArgumentException>(() => ServiceCycleProfileSegmentCodec.Encode(
            new ServiceCycleProfileSessionId(1), 0, 1, in calibration, new[] { measured }, destination));
    }
}
