using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

internal static class FullTraceManifestCodec
{
    internal const ushort SchemaVersion = 1;
    internal const int ManifestBytes = 160;

    private const uint DiagnosticOnly = 1;
    private const int ChecksumOffset = 128;
    private static readonly byte[] Magic = { (byte)'O', (byte)'S', (byte)'C', (byte)'M' };

    internal static int Encode(in FullTraceManifestDocument document, Span<byte> destination)
    {
        Validate(in document);
        if (destination.Length < ManifestBytes)
            throw new ArgumentException("The destination is too small.", nameof(destination));

        var output = destination.Slice(0, ManifestBytes);
        output.Clear();
        Magic.CopyTo(output);
        WriteU16(output, 4, SchemaVersion);
        WriteU16(output, 6, ManifestBytes);
        WriteU16(output, 8, FullTraceSegmentCodec.SchemaVersion);
        WriteU16(output, 10, ServiceCycleTraceCodec.SchemaVersion);
        WriteU16(output, 12, ServiceCycleSemanticEventV7Codec.RecordBytes);
        WriteU32(output, 16, (uint)document.Completeness);
        WriteU32(output, 20, (uint)document.Reason);
        WriteU32(output, 24, DiagnosticOnly);
        WriteU64(output, 32, document.Session.Value);
        WriteU64(output, 40, document.SemanticSession.Value);
        WriteU64(output, 48, checked((ulong)document.ServiceCapacity));
        WriteU64(output, 56, document.SegmentCount);
        WriteU64(output, 64, document.FirstSemanticSequence);
        WriteU64(output, 72, document.AcceptedRecords);
        WriteU64(output, 80, document.WrittenRecords);
        WriteU64(output, 88, document.FirstIncompleteTransportSequence);
        WriteU64(output, 96, document.FirstIncompleteSemanticSequence);
        WriteI64(output, 104, document.FirstTimestampTicks);
        WriteI64(output, 112, document.LastTimestampTicks);
        WriteU64(output, 120, document.SegmentBytes);
        WriteU32(output, ChecksumOffset, TraceCrc32.ComputeExcluding(output, ChecksumOffset, 4));
        return ManifestBytes;
    }

    internal static FullTraceManifestDocument Decode(ReadOnlySpan<byte> source)
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

    private static FullTraceManifestDocument DecodeCore(ReadOnlySpan<byte> source)
    {
        if (source.Length != ManifestBytes || !source.Slice(0, 4).SequenceEqual(Magic) ||
            ReadU16(source, 4) != SchemaVersion || ReadU16(source, 6) != ManifestBytes ||
            ReadU16(source, 8) != FullTraceSegmentCodec.SchemaVersion ||
            ReadU16(source, 10) != ServiceCycleTraceCodec.SchemaVersion ||
            ReadU16(source, 12) != ServiceCycleSemanticEventV7Codec.RecordBytes ||
            ReadU16(source, 14) != 0 || ReadU32(source, 24) != DiagnosticOnly || ReadU32(source, 28) != 0 ||
            !AllZero(source.Slice(132, 28)) ||
            ReadU32(source, ChecksumOffset) != TraceCrc32.ComputeExcluding(source, ChecksumOffset, 4))
            throw Invalid();

        var completenessValue = ReadU32(source, 16);
        var reasonValue = ReadU32(source, 20);
        if (completenessValue is < (uint)FullTraceCompleteness.Complete or > (uint)FullTraceCompleteness.Incomplete ||
            reasonValue is < (uint)FullTraceTerminalReason.UserStopped or > (uint)FullTraceTerminalReason.SemanticFault)
            throw Invalid();

        FullTraceManifestDocument document;
        try
        {
            var serviceCapacityValue = ReadU64(source, 48);
            if (serviceCapacityValue > int.MaxValue) throw Invalid();
            document = new FullTraceManifestDocument(
                (FullTraceCompleteness)completenessValue,
                (FullTraceTerminalReason)reasonValue,
                new FullTraceSessionId(ReadU64(source, 32)),
                new ServiceCycleTraceSessionId(ReadU64(source, 40)),
                (int)serviceCapacityValue,
                ReadU64(source, 56),
                ReadU64(source, 64),
                ReadU64(source, 72),
                ReadU64(source, 80),
                ReadU64(source, 88),
                ReadU64(source, 96),
                ReadI64(source, 104),
                ReadI64(source, 112),
                ReadU64(source, 120));
            Validate(in document);
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        return document;
    }

    private static void Validate(in FullTraceManifestDocument document)
    {
        if (!document.Session.IsValid) throw new ArgumentException("A valid full-trace session is required.");
        if (!document.SemanticSession.IsValid) throw new ArgumentException("A valid semantic session is required.");
        if (document.ServiceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(document));
        if (document.FirstSemanticSequence == 0 ||
            document.FirstSemanticSequence > ServiceCycleTraceEventId.MaximumSequence ||
            document.WrittenRecords > document.AcceptedRecords ||
            document.AcceptedRecords > long.MaxValue)
            throw new ArgumentException("The record range is invalid.", nameof(document));
        if (document.AcceptedRecords != 0 &&
            checked(document.FirstSemanticSequence + document.AcceptedRecords - 1) >
            ServiceCycleTraceEventId.MaximumSequence)
            throw new ArgumentException("The semantic sequence range is exhausted.", nameof(document));

        var expectedSegmentBytes = checked(
            document.WrittenRecords * ServiceCycleSemanticEventV7Codec.RecordBytes +
            document.SegmentCount * (FullTraceSegmentCodec.HeaderBytes + FullTraceSegmentCodec.FooterBytes));
        if (document.WrittenRecords == 0 &&
                (document.SegmentCount != 0 || document.SegmentBytes != 0 ||
                    document.FirstTimestampTicks != 0 || document.LastTimestampTicks != 0) ||
            document.WrittenRecords != 0 &&
                (document.SegmentCount == 0 || document.SegmentCount > document.WrittenRecords ||
                    document.SegmentBytes != expectedSegmentBytes ||
                    document.LastTimestampTicks < document.FirstTimestampTicks))
            throw new ArgumentException("The committed segment evidence is inconsistent.", nameof(document));

        var complete = document.Completeness == FullTraceCompleteness.Complete;
        if (complete)
        {
            if (document.Reason is not (FullTraceTerminalReason.UserStopped or FullTraceTerminalReason.RuntimeShutdown) ||
                document.AcceptedRecords != document.WrittenRecords ||
                document.FirstIncompleteTransportSequence != 0 || document.FirstIncompleteSemanticSequence != 0)
                throw new ArgumentException("Complete trace evidence is inconsistent.", nameof(document));
        }
        else if (document.Completeness != FullTraceCompleteness.Incomplete ||
            document.Reason == FullTraceTerminalReason.UserStopped ||
            document.FirstIncompleteTransportSequence != document.WrittenRecords + 1 ||
            document.FirstIncompleteSemanticSequence != checked(
                document.FirstSemanticSequence + document.WrittenRecords))
        {
            throw new ArgumentException("Incomplete trace evidence is inconsistent.", nameof(document));
        }
    }

    private static bool AllZero(ReadOnlySpan<byte> source)
    {
        for (var index = 0; index < source.Length; index++)
            if (source[index] != 0) return false;
        return true;
    }

    private static FormatException Invalid() => new("Invalid manual full-trace manifest.");
    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    private static long ReadI64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    private static void WriteU16(Span<byte> bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    private static void WriteU32(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    private static void WriteU64(Span<byte> bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    private static void WriteI64(Span<byte> bytes, int offset, long value) => BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);
}
