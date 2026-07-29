using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using Xunit;
using static OrbModding.ProfileTests.ServiceCycleProfileTestData;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileManifestCodecTests
{
    [Theory]
    [InlineData(1u, 1u, 2, 2, 0)]
    [InlineData(2u, 5u, 3, 2, 3)]
    public void ManifestRoundTripsTerminalEvidence(
        uint completenessValue,
        uint reasonValue,
        ulong accepted,
        ulong written,
        ulong firstIncomplete)
    {
        var completeness = (ServiceCycleProfileCompleteness)completenessValue;
        var reason = (ServiceCycleProfileTerminalReason)reasonValue;
        var calibration = Calibration(traceActive: true);
        var segmentBytes = written * ServiceCycleProfileRecordCodec.RecordBytes +
            ServiceCycleProfileSegmentCodec.HeaderBytes + ServiceCycleProfileSegmentCodec.FooterBytes;
        var expected = new ServiceCycleProfileManifestDocument(
            completeness,
            reason,
            new ServiceCycleProfileSessionId(9),
            in calibration,
            segmentCount: 1,
            accepted,
            written,
            firstIncomplete,
            segmentBytes,
            minimumStartedAtRawTicks: 100,
            maximumStartedAtRawTicks: 200);
        var bytes = new byte[ServiceCycleProfileManifestCodec.ManifestBytes];

        ServiceCycleProfileManifestCodec.Encode(in expected, bytes);
        var actual = ServiceCycleProfileManifestCodec.Decode(bytes);

        Assert.Equal(completeness, actual.Completeness);
        Assert.Equal(reason, actual.Reason);
        Assert.Equal(accepted, actual.AcceptedRecords);
        Assert.Equal(written, actual.WrittenRecords);
        Assert.Equal(firstIncomplete, actual.FirstIncompleteSequence);
        Assert.Equal(segmentBytes, actual.SegmentBytes);
        Assert.Equal(BuildId, actual.Calibration.BuildId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(140)]
    [InlineData(136)]
    public void HeaderReservedAndChecksumMutationsFailClosed(int offset)
    {
        var calibration = Calibration();
        var document = new ServiceCycleProfileManifestDocument(
            ServiceCycleProfileCompleteness.Complete,
            ServiceCycleProfileTerminalReason.UserStopped,
            new ServiceCycleProfileSessionId(1),
            in calibration,
            0, 0, 0, 0, 0, 0, 0);
        var bytes = new byte[ServiceCycleProfileManifestCodec.ManifestBytes];
        ServiceCycleProfileManifestCodec.Encode(in document, bytes);
        bytes[offset] ^= 1;

        Assert.Throws<FormatException>(() => ServiceCycleProfileManifestCodec.Decode(bytes));
    }

    [Fact]
    public void CompletenessCannotHideMissingRecords()
    {
        var calibration = Calibration();
        var document = new ServiceCycleProfileManifestDocument(
            ServiceCycleProfileCompleteness.Complete,
            ServiceCycleProfileTerminalReason.UserStopped,
            new ServiceCycleProfileSessionId(1),
            in calibration,
            segmentCount: 1,
            acceptedRecords: 2,
            writtenRecords: 1,
            firstIncompleteSequence: 0,
            segmentBytes: ServiceCycleProfileRecordCodec.RecordBytes +
                ServiceCycleProfileSegmentCodec.HeaderBytes + ServiceCycleProfileSegmentCodec.FooterBytes,
            minimumStartedAtRawTicks: 1,
            maximumStartedAtRawTicks: 1);

        Assert.Throws<ArgumentException>(() =>
            ServiceCycleProfileManifestCodec.Encode(
                in document,
                new byte[ServiceCycleProfileManifestCodec.ManifestBytes]));
    }
}
