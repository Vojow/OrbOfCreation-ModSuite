using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.FullTrace.Format;

public sealed class FullTraceFormatCodecTests
{
    [Fact]
    public void ExtractedSemanticRecordCodecRemainsByteIdenticalForEveryEventKind()
    {
        var events = ServiceCycleTraceFixtures.EveryEventKind();
        var trace = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(ServiceCycleTraceFixtures.Session, default, events, trace);

        var record = new byte[ServiceCycleSemanticEventV7Codec.RecordBytes];
        for (var index = 0; index < events.Length; index++)
        {
            Array.Fill(record, (byte)0xff);
            ServiceCycleSemanticEventV7Codec.Write(record, in events[index]);
            Assert.Equal(
                trace.AsSpan(
                    ServiceCycleTraceCodec.HeaderBytes + index * ServiceCycleTraceCodec.RecordBytes,
                    ServiceCycleTraceCodec.RecordBytes).ToArray(),
                record);
            Assert.Equal(events[index], ServiceCycleSemanticEventV7Codec.Read(record));
        }
    }

    [Fact]
    public void SegmentsRoundTripCrossSegmentParentsWithoutInventingDrops()
    {
        var session = new FullTraceSessionId(700);
        var semantic = new ServiceCycleTraceSessionId(701);
        var firstEvents = new[]
        {
            ServiceCycleTraceFixtures.Event(10, eventSession: semantic),
            ServiceCycleTraceFixtures.Event(11, parentSequence: 10, eventSession: semantic),
        };
        var secondEvents = new[]
        {
            ServiceCycleTraceFixtures.Event(12, parentSequence: 10, eventSession: semantic),
            ServiceCycleTraceFixtures.Event(13, parentSequence: 12, eventSession: semantic),
        };

        var first = EncodeSegment(session, semantic, 0, 1, firstEvents);
        var second = EncodeSegment(session, semantic, 1, 3, secondEvents);
        var firstDocument = FullTraceSegmentCodec.Decode(first);
        var secondDocument = FullTraceSegmentCodec.Decode(second);

        Assert.Equal(firstEvents, firstDocument.Events);
        Assert.Equal(secondEvents, secondDocument.Events);
        Assert.Equal(10UL, secondDocument.Events[0].Parent.Sequence);
        Assert.Equal(3UL, secondDocument.FirstTransportSequence);
    }

    [Fact]
    public void SegmentDecoderRejectsCorruptionReservedDataAndSequenceGaps()
    {
        var semantic = new ServiceCycleTraceSessionId(801);
        var valid = EncodeSegment(
            new FullTraceSessionId(800),
            semantic,
            0,
            1,
            new[]
            {
                ServiceCycleTraceFixtures.Event(1, eventSession: semantic),
                ServiceCycleTraceFixtures.Event(2, parentSequence: 1, eventSession: semantic),
            });

        Assert.Throws<FormatException>(() => FullTraceSegmentCodec.Decode(Mutated(valid, bytes => bytes[0] ^= 1)));
        Assert.Throws<FormatException>(() => FullTraceSegmentCodec.Decode(Mutated(valid, bytes => bytes[100] ^= 1)));
        Assert.Throws<FormatException>(() => FullTraceSegmentCodec.Decode(MutatedAndResealed(valid, bytes => bytes[88] = 1)));
        Assert.Throws<FormatException>(() => FullTraceSegmentCodec.Decode(MutatedAndResealed(valid, bytes =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(FullTraceSegmentCodec.HeaderBytes + ServiceCycleTraceCodec.RecordBytes + 8, 8),
                3);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 16, 8), 3);
        })));
    }

    [Fact]
    public void ManifestRoundTripsCompleteAndIncompleteTerminalEvidence()
    {
        var complete = Manifest(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            accepted: 8,
            written: 8,
            incompleteTransport: 0,
            incompleteSemantic: 0);
        var incomplete = Manifest(
            FullTraceCompleteness.Incomplete,
            FullTraceTerminalReason.BufferExhausted,
            accepted: 8,
            written: 8,
            incompleteTransport: 9,
            incompleteSemantic: 19);
        var zeroTimestamp = new FullTraceManifestDocument(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            new FullTraceSessionId(900),
            new ServiceCycleTraceSessionId(901),
            7,
            1,
            11,
            1,
            1,
            0,
            0,
            0,
            0,
            ServiceCycleTraceCodec.RecordBytes + FullTraceSegmentCodec.HeaderBytes +
                FullTraceSegmentCodec.FooterBytes);

        AssertManifestRoundTrip(complete);
        AssertManifestRoundTrip(incomplete);
        AssertManifestRoundTrip(zeroTimestamp);

        var corrupt = EncodeManifest(complete);
        corrupt[140] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(
            corrupt.AsSpan(128, 4),
            TraceCrc32.ComputeExcluding(corrupt, 128, 4));
        Assert.Throws<FormatException>(() => FullTraceManifestCodec.Decode(corrupt));

        Assert.Throws<FormatException>(() => FullTraceManifestCodec.Decode(
            MutatedManifestAndResealed(complete, bytes =>
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(120, 8), 999))));
        Assert.Throws<FormatException>(() => FullTraceManifestCodec.Decode(
            MutatedManifestAndResealed(incomplete, bytes =>
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(88, 8), 8))));
        Assert.Throws<FormatException>(() => FullTraceManifestCodec.Decode(
            MutatedManifestAndResealed(complete, bytes =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(72, 8), long.MaxValue);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80, 8), long.MaxValue);
            })));
    }

    private static byte[] EncodeSegment(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semantic,
        ulong ordinal,
        ulong firstTransportSequence,
        ServiceCycleSemanticEvent[] events)
    {
        var bytes = new byte[FullTraceSegmentCodec.GetEncodedLength(events.Length)];
        FullTraceSegmentCodec.Encode(session, semantic, ordinal, firstTransportSequence, 7, events, bytes);
        return bytes;
    }

    private static FullTraceManifestDocument Manifest(
        FullTraceCompleteness completeness,
        FullTraceTerminalReason reason,
        ulong accepted,
        ulong written,
        ulong incompleteTransport,
        ulong incompleteSemantic) => new(
            completeness,
            reason,
            new FullTraceSessionId(900),
            new ServiceCycleTraceSessionId(901),
            7,
            1,
            11,
            accepted,
            written,
            incompleteTransport,
            incompleteSemantic,
            100,
            200,
            checked(written * ServiceCycleTraceCodec.RecordBytes + FullTraceSegmentCodec.HeaderBytes +
                FullTraceSegmentCodec.FooterBytes));

    private static void AssertManifestRoundTrip(FullTraceManifestDocument expected)
    {
        var actual = FullTraceManifestCodec.Decode(EncodeManifest(expected));
        Assert.Equal(expected.Completeness, actual.Completeness);
        Assert.Equal(expected.Reason, actual.Reason);
        Assert.Equal(expected.Session, actual.Session);
        Assert.Equal(expected.SemanticSession, actual.SemanticSession);
        Assert.Equal(expected.AcceptedRecords, actual.AcceptedRecords);
        Assert.Equal(expected.WrittenRecords, actual.WrittenRecords);
        Assert.Equal(expected.FirstIncompleteTransportSequence, actual.FirstIncompleteTransportSequence);
        Assert.Equal(expected.FirstIncompleteSemanticSequence, actual.FirstIncompleteSemanticSequence);
    }

    private static byte[] EncodeManifest(FullTraceManifestDocument document)
    {
        var bytes = new byte[FullTraceManifestCodec.ManifestBytes];
        FullTraceManifestCodec.Encode(in document, bytes);
        return bytes;
    }

    private static byte[] Mutated(byte[] source, Action<byte[]> mutation)
    {
        var copy = (byte[])source.Clone();
        mutation(copy);
        return copy;
    }

    private static byte[] MutatedAndResealed(byte[] source, Action<byte[]> mutation)
    {
        var copy = Mutated(source, mutation);
        var checksumOffset = copy.Length - 8;
        BinaryPrimitives.WriteUInt32LittleEndian(
            copy.AsSpan(checksumOffset, 4),
            TraceCrc32.ComputeExcluding(copy, checksumOffset, 4));
        return copy;
    }

    private static byte[] MutatedManifestAndResealed(
        FullTraceManifestDocument source,
        Action<byte[]> mutation)
    {
        var copy = EncodeManifest(source);
        mutation(copy);
        BinaryPrimitives.WriteUInt32LittleEndian(
            copy.AsSpan(128, 4),
            TraceCrc32.ComputeExcluding(copy, 128, 4));
        return copy;
    }
}
