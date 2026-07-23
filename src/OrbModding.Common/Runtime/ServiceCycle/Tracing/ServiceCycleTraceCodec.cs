using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public static class ServiceCycleTraceCodec
{
    public const ushort SchemaVersion = 5;
    public const int HeaderBytes = 64;
    public const int RecordBytes = ServiceCycleSemanticEventV5Codec.RecordBytes;
    public const int DefaultMaximumRecords = 1_000_000;

    private const ushort IncompleteFlag = 1;
    private const int ChecksumOffset = 56;
    private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'C', (byte)'E' };

    public static int GetEncodedLength(int eventCount)
    {
        if (eventCount < 0) throw new ArgumentOutOfRangeException(nameof(eventCount));
        return checked(HeaderBytes + checked(eventCount * RecordBytes));
    }

    public static int Encode(
        ServiceCycleTraceSessionId session,
        ServiceCycleTraceDropRange dropped,
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        Span<byte> destination)
        => Encode(session, dropped, InferServiceCapacity(events), events, destination);

    public static int Encode(
        ServiceCycleTraceSessionId session,
        ServiceCycleTraceDropRange dropped,
        int serviceCapacity,
        ReadOnlySpan<ServiceCycleSemanticEvent> events,
        Span<byte> destination)
    {
        if (!session.IsValid) throw new ArgumentException("A valid trace session is required.", nameof(session));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        var hasDropData = dropped.Session.IsValid || dropped.FirstSequence != 0 || dropped.LastSequence != 0;
        if (hasDropData && (!dropped.IsPresent || dropped.Session != session || dropped.FirstSequence == 0 ||
            dropped.LastSequence < dropped.FirstSequence ||
            dropped.LastSequence > ServiceCycleTraceEventId.MaximumSequence))
            throw new ArgumentException("The drop range is invalid or belongs to another trace session.", nameof(dropped));
        var length = GetEncodedLength(events.Length);
        if (destination.Length < length) throw new ArgumentException("The destination is too small.", nameof(destination));

        for (var i = 0; i < events.Length; i++)
        {
            var item = events[i];
            if (!item.Id.IsValid || item.Id.Session != session)
                throw new ArgumentException("An event identity is invalid or belongs to another trace session.", nameof(events));
            var parentIsDefault = !item.Parent.Session.IsValid && item.Parent.Sequence == 0;
            if (!parentIsDefault && (!item.Parent.IsValid || item.Parent.Session != session))
                throw new ArgumentException("A causal parent identity is invalid or belongs to another trace session.", nameof(events));
        }

        var firstSequence = events.Length == 0 ? 0 : events[0].Id.Sequence;
        if (!dropped.IsPresent && firstSequence > 1 ||
            dropped.IsPresent && (dropped.FirstSequence != 1 ||
                events.Length != 0 && dropped.LastSequence != firstSequence - 1))
            throw new ArgumentException("The stream must account for every event since its session root.", nameof(events));

        ulong previousSequence = 0;
        for (var i = 0; i < events.Length; i++)
        {
            var item = events[i];
            if (i != 0 && item.Id.Sequence != checked(previousSequence + 1))
                throw new ArgumentException("Semantic stream event identities must be contiguous.", nameof(events));
            if (item.HasParent && (item.Parent.Sequence >= item.Id.Sequence ||
                item.Parent.Sequence < firstSequence && (!dropped.IsPresent ||
                    item.Parent.Sequence < dropped.FirstSequence || item.Parent.Sequence > dropped.LastSequence)))
                throw new ArgumentException("A causal parent must be earlier and present or explicitly overwritten.", nameof(events));
            if (item.HasParent && item.Parent.Sequence >= firstSequence)
            {
                var parentIndex = checked((int)(item.Parent.Sequence - firstSequence));
                if (events[parentIndex].Payload.TimestampTicks > item.Payload.TimestampTicks)
                    throw new ArgumentException("A causal parent cannot occur later than its child.", nameof(events));
            }
            var payload = item.Payload;
            if (payload.Service > (ulong)serviceCapacity)
                throw new ArgumentException("An event service exceeds the declared topology.", nameof(events));
            ServiceCycleSemanticPayloadValidation.EnsureValid(item.Kind, in payload);
            previousSequence = item.Id.Sequence;
        }

        var output = destination.Slice(0, length);
        output.Clear();
        Magic.AsSpan().CopyTo(output);
        WriteU16(output, 4, SchemaVersion);
        WriteU16(output, 6, HeaderBytes);
        WriteU16(output, 8, RecordBytes);
        WriteU16(output, 10, dropped.IsPresent ? IncompleteFlag : (ushort)0);
        WriteU64(output, 12, session.Value);
        WriteU64(output, 20, firstSequence);
        WriteU32(output, 28, checked((uint)events.Length));
        WriteU32(output, 32, checked((uint)(events.Length * RecordBytes)));
        WriteU64(output, 36, dropped.IsPresent ? dropped.FirstSequence : 0);
        WriteU64(output, 44, dropped.IsPresent ? dropped.LastSequence : 0);
        WriteU32(output, 52, checked((uint)length));
        WriteU32(output, 60, checked((uint)serviceCapacity));

        for (var i = 0; i < events.Length; i++)
        {
            var item = events[i];
            ServiceCycleSemanticEventV5Codec.Write(
                output.Slice(HeaderBytes + i * RecordBytes, RecordBytes),
                in item);
        }

        WriteU32(output, ChecksumOffset, TraceCrc32.ComputeExcluding(output, ChecksumOffset, 4));
        return length;
    }

    public static ServiceCycleTraceDocument Decode(
        ReadOnlySpan<byte> source,
        int maximumRecords = DefaultMaximumRecords)
    {
        if (maximumRecords < 0) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        if (source.Length < HeaderBytes) throw Invalid();
        if (!source.Slice(0, 4).SequenceEqual(Magic)) throw Invalid();
        var schema = ReadU16(source, 4);
        if (schema != SchemaVersion) throw Invalid();
        if (ReadU16(source, 6) != HeaderBytes || ReadU16(source, 8) != RecordBytes) throw Invalid();
        var flags = ReadU16(source, 10);
        if ((flags & ~IncompleteFlag) != 0) throw Invalid();
        var serviceCapacityValue = ReadU32(source, 60);
        if (serviceCapacityValue == 0 || serviceCapacityValue > int.MaxValue) throw Invalid();
        var serviceCapacity = (int)serviceCapacityValue;
        var sessionValue = ReadU64(source, 12);
        if (sessionValue == 0) throw Invalid();
        var firstSequence = ReadU64(source, 20);
        var countValue = ReadU32(source, 28);
        if (countValue > (uint)maximumRecords || countValue > int.MaxValue) throw Invalid();
        var count = (int)countValue;
        var payloadBytes = ReadU32(source, 32);
        var expectedPayloadBytes = checked((ulong)countValue * RecordBytes);
        if (payloadBytes != expectedPayloadBytes) throw Invalid();
        var expectedLength = checked((ulong)HeaderBytes + expectedPayloadBytes);
        if (expectedLength > int.MaxValue || ReadU32(source, 52) != expectedLength || source.Length != (int)expectedLength)
            throw Invalid();
        var dropFirst = ReadU64(source, 36);
        var dropLast = ReadU64(source, 44);
        var incomplete = (flags & IncompleteFlag) != 0;
        if (incomplete != (dropFirst != 0 || dropLast != 0) ||
            incomplete && (dropFirst == 0 || dropLast < dropFirst ||
                dropLast > ServiceCycleTraceEventId.MaximumSequence) ||
            !incomplete && (dropFirst != 0 || dropLast != 0))
            throw Invalid();
        if (count == 0 && firstSequence != 0 || count != 0 && firstSequence == 0) throw Invalid();
        if (!incomplete && firstSequence > 1 ||
            incomplete && (dropFirst != 1 || count != 0 && dropLast != firstSequence - 1))
            throw Invalid();
        var checksum = ReadU32(source, ChecksumOffset);
        if (checksum != TraceCrc32.ComputeExcluding(source, ChecksumOffset, 4)) throw Invalid();

        var session = new ServiceCycleTraceSessionId(sessionValue);
        var dropped = incomplete ? new ServiceCycleTraceDropRange(session, dropFirst, dropLast) : default;
        var events = new ServiceCycleSemanticEvent[count];
        for (var i = 0; i < count; i++)
        {
            events[i] = ServiceCycleSemanticEventV5Codec.Read(
                source.Slice(HeaderBytes + i * RecordBytes, RecordBytes));
            if (events[i].Payload.Service > (ulong)serviceCapacity) throw Invalid();
            if (events[i].Id.Session != session) throw Invalid();
            if (i == 0 && events[i].Id.Sequence != firstSequence) throw Invalid();
            if (i != 0 && events[i].Id.Sequence != events[i - 1].Id.Sequence + 1) throw Invalid();
        }
        var document = new ServiceCycleTraceDocument(schema, session, dropped, serviceCapacity, events);
        if (!ServiceCycleTraceGraphValidator.Validate(document).IsValid) throw Invalid();
        return document;
    }

    private static FormatException Invalid() => new("Invalid service-cycle semantic trace.");
    private static ushort ReadU16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(o, 2));
    private static uint ReadU32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(o, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(o, 8));
    private static void WriteU16(Span<byte> b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(o, 2), v);
    private static void WriteU32(Span<byte> b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(o, 4), v);
    private static void WriteU64(Span<byte> b, int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(o, 8), v);

    private static int InferServiceCapacity(ReadOnlySpan<ServiceCycleSemanticEvent> events)
    {
        ulong maximum = 0;
        for (var index = 0; index < events.Length; index++)
            if (events[index].Payload.Service > maximum) maximum = events[index].Payload.Service;
        return maximum == 0 ? 1 : checked((int)maximum);
    }
}
