using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

internal static class FullTraceSegmentCodec
{
    internal const ushort SchemaVersion = 1;
    internal const int HeaderBytes = 96;
    internal const int FooterBytes = 48;
    // A segment stays under 1 MiB: (1,048,576 - 96 header - 48 footer) / 288 record bytes = 3,640.
    internal const int MaximumRecords = 3_640;

    private const int FooterChecksumOffset = 40;
    private static readonly byte[] HeaderMagic = { (byte)'O', (byte)'S', (byte)'C', (byte)'S' };
    private static readonly byte[] FooterMagic = { (byte)'O', (byte)'S', (byte)'C', (byte)'F' };

    internal static int GetEncodedLength(int recordCount)
    {
        if (recordCount is <= 0 or > MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return checked(HeaderBytes + checked(recordCount * ServiceCycleSemanticEventV7Codec.RecordBytes) + FooterBytes);
    }

    internal static int Encode(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        ulong ordinal,
        ulong firstTransportSequence,
        int serviceCapacity,
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        Span<byte> destination)
    {
        ValidateEnvelope(session, semanticSession, firstTransportSequence, serviceCapacity, events.Length);
        ValidateEvents(semanticSession, serviceCapacity, events);
        var length = GetEncodedLength(events.Length);
        if (destination.Length < length) throw new ArgumentException("The destination is too small.", nameof(destination));

        var output = destination.Slice(0, length);
        output.Clear();
        HeaderMagic.CopyTo(output);
        WriteU16(output, 4, SchemaVersion);
        WriteU16(output, 6, HeaderBytes);
        WriteU16(output, 8, ServiceCycleTraceCodec.SchemaVersion);
        WriteU16(output, 10, ServiceCycleSemanticEventV7Codec.RecordBytes);
        WriteU64(output, 16, session.Value);
        WriteU64(output, 24, semanticSession.Value);
        WriteU64(output, 32, ordinal);
        WriteU64(output, 40, firstTransportSequence);
        WriteU64(output, 48, events[0].Id.Sequence);
        WriteU64(output, 56, checked((ulong)events.Length));
        WriteU64(output, 64, checked((ulong)events.Length * ServiceCycleSemanticEventV7Codec.RecordBytes));
        WriteU64(output, 72, checked((ulong)serviceCapacity));
        WriteU64(output, 80, checked((ulong)length));

        for (var index = 0; index < events.Length; index++)
        {
            ServiceCycleSemanticEventV7Codec.Write(
                output.Slice(
                    HeaderBytes + index * ServiceCycleSemanticEventV7Codec.RecordBytes,
                    ServiceCycleSemanticEventV7Codec.RecordBytes),
                in events[index]);
        }

        var footerOffset = length - FooterBytes;
        FooterMagic.CopyTo(output.Slice(footerOffset));
        WriteU16(output, footerOffset + 4, SchemaVersion);
        WriteU16(output, footerOffset + 6, FooterBytes);
        WriteU64(output, footerOffset + 8, session.Value);
        WriteU64(output, footerOffset + 16, ordinal);
        WriteU64(output, footerOffset + 24, checked(firstTransportSequence + (ulong)events.Length - 1));
        WriteU64(output, footerOffset + 32, events[^1].Id.Sequence);
        var checksumOffset = footerOffset + FooterChecksumOffset;
        WriteU32(output, checksumOffset, TraceCrc32.ComputeExcluding(output, checksumOffset, 4));
        return length;
    }

    internal static FullTraceSegmentDocument Decode(ReadOnlySpan<byte> source)
    {
        try
        {
            return DecodeCore(source);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private static FullTraceSegmentDocument DecodeCore(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderBytes + ServiceCycleSemanticEventV7Codec.RecordBytes + FooterBytes ||
            !source.Slice(0, 4).SequenceEqual(HeaderMagic) ||
            ReadU16(source, 4) != SchemaVersion || ReadU16(source, 6) != HeaderBytes ||
            ReadU16(source, 8) != ServiceCycleTraceCodec.SchemaVersion ||
            ReadU16(source, 10) != ServiceCycleSemanticEventV7Codec.RecordBytes ||
            ReadU32(source, 12) != 0 || ReadU64(source, 88) != 0)
            throw Invalid();

        var sessionValue = ReadU64(source, 16);
        var semanticSessionValue = ReadU64(source, 24);
        var ordinal = ReadU64(source, 32);
        var firstTransportSequence = ReadU64(source, 40);
        var firstSemanticSequence = ReadU64(source, 48);
        var countValue = ReadU64(source, 56);
        var payloadBytes = ReadU64(source, 64);
        var serviceCapacityValue = ReadU64(source, 72);
        var totalBytes = ReadU64(source, 80);
        if (sessionValue == 0 || semanticSessionValue == 0 || firstTransportSequence == 0 ||
            firstSemanticSequence == 0 || countValue is 0 or > MaximumRecords || countValue > int.MaxValue ||
            serviceCapacityValue is 0 or > int.MaxValue ||
            payloadBytes != checked(countValue * ServiceCycleSemanticEventV7Codec.RecordBytes) ||
            totalBytes != checked((ulong)HeaderBytes + payloadBytes + FooterBytes) ||
            totalBytes != checked((ulong)source.Length))
            throw Invalid();

        var footerOffset = source.Length - FooterBytes;
        if (!source.Slice(footerOffset, 4).SequenceEqual(FooterMagic) ||
            ReadU16(source, footerOffset + 4) != SchemaVersion ||
            ReadU16(source, footerOffset + 6) != FooterBytes ||
            ReadU64(source, footerOffset + 8) != sessionValue ||
            ReadU64(source, footerOffset + 16) != ordinal ||
            ReadU64(source, footerOffset + 24) != checked(firstTransportSequence + countValue - 1) ||
            ReadU32(source, footerOffset + 44) != 0)
            throw Invalid();
        var checksumOffset = footerOffset + FooterChecksumOffset;
        if (ReadU32(source, checksumOffset) != TraceCrc32.ComputeExcluding(source, checksumOffset, 4))
            throw Invalid();

        var session = new FullTraceSessionId(sessionValue);
        var semanticSession = new ServiceCycleTraceSessionId(semanticSessionValue);
        var count = (int)countValue;
        var serviceCapacity = (int)serviceCapacityValue;
        var events = new ServiceCycleSemanticEvent[count];
        for (var index = 0; index < count; index++)
        {
            events[index] = ServiceCycleSemanticEventV7Codec.Read(source.Slice(
                HeaderBytes + index * ServiceCycleSemanticEventV7Codec.RecordBytes,
                ServiceCycleSemanticEventV7Codec.RecordBytes));
        }
        ValidateEvents(semanticSession, serviceCapacity, events);
        if (events[0].Id.Sequence != firstSemanticSequence ||
            events[^1].Id.Sequence != ReadU64(source, footerOffset + 32))
            throw Invalid();
        return new FullTraceSegmentDocument(
            session, semanticSession, ordinal, firstTransportSequence, serviceCapacity, events);
    }

    private static void ValidateEnvelope(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        ulong firstTransportSequence,
        int serviceCapacity,
        int eventCount)
    {
        if (!session.IsValid) throw new ArgumentException("A valid full-trace session is required.", nameof(session));
        if (!semanticSession.IsValid)
            throw new ArgumentException("A valid semantic session is required.", nameof(semanticSession));
        if (firstTransportSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstTransportSequence));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _ = GetEncodedLength(eventCount);
        _ = checked(firstTransportSequence + (ulong)eventCount - 1);
    }

    private static void ValidateEvents(
        ServiceCycleTraceSessionId semanticSession,
        int serviceCapacity,
        ReadOnlySpan<ServiceCycleSemanticEvent> events)
    {
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            if (!item.Id.IsValid || item.Id.Session != semanticSession ||
                index != 0 && item.Id.Sequence != events[index - 1].Id.Sequence + 1 ||
                item.Payload.Service > (ulong)serviceCapacity)
                throw new ArgumentException("The semantic segment is not contiguous within its declared topology.", nameof(events));
            if (item.HasParent && (item.Parent.Session != semanticSession || item.Parent.Sequence >= item.Id.Sequence))
                throw new ArgumentException("A semantic parent must be earlier in the same semantic session.", nameof(events));
            if (item.HasParent && item.Parent.Sequence >= events[0].Id.Sequence)
            {
                var parentIndex = checked((int)(item.Parent.Sequence - events[0].Id.Sequence));
                if (events[parentIndex].Payload.TimestampTicks > item.Payload.TimestampTicks)
                    throw new ArgumentException("A semantic parent cannot occur after its child.", nameof(events));
            }
            var payload = item.Payload;
            ServiceCycleSemanticPayloadValidation.EnsureValid(item.Kind, in payload);
        }
    }

    private static FormatException Invalid() => new("Invalid manual full-trace segment.");
    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteU16(Span<byte> bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    private static void WriteU32(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
}
