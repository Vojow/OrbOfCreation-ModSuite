using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalSegmentCodecTests
{
    [Fact]
    public void SegmentRoundTripsEnvelopeAndRecords()
    {
        var records = new[]
        {
            DecisionJournalRecord.Decision(CreateObservation(1, 20)),
            DecisionJournalRecord.Decision(CreateObservation(2, 10)),
        };
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(records.Length)];

        var written = DecisionJournalSegmentCodec.Encode(
            new DecisionJournalRunId(7),
            3,
            41,
            records,
            bytes);
        var actual = DecisionJournalSegmentCodec.Decode(bytes);

        Assert.Equal(bytes.Length, written);
        Assert.Equal(new DecisionJournalRunId(7), actual.Run);
        Assert.Equal((ulong)3, actual.Ordinal);
        Assert.Equal((ulong)41, actual.FirstRecordSequence);
        Assert.Equal(2, actual.Records.Length);
        Assert.Equal((ulong)1, actual.Records[0].FirstCycle);
        Assert.Equal((ulong)2, actual.Records[1].FirstCycle);
    }

    [Fact]
    public void ChecksumMutationIsRejected()
    {
        var records = new[] { DecisionJournalRecord.Decision(CreateObservation(1, 10)) };
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(records.Length)];
        DecisionJournalSegmentCodec.Encode(new DecisionJournalRunId(1), 0, 1, records, bytes);
        bytes[DecisionJournalSegmentCodec.HeaderBytes + 12] ^= 1;

        Assert.Throws<FormatException>(() => DecisionJournalSegmentCodec.Decode(bytes));
    }

    [Fact]
    public void ReservedHeaderMutationIsRejected()
    {
        var records = new[] { DecisionJournalRecord.Decision(CreateObservation(1, 10)) };
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(records.Length)];
        DecisionJournalSegmentCodec.Encode(new DecisionJournalRunId(1), 0, 1, records, bytes);
        bytes[72] = 1;

        Assert.Throws<FormatException>(() => DecisionJournalSegmentCodec.Decode(bytes));
    }
}
