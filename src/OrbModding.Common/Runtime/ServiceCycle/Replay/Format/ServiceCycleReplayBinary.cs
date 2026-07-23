using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayBinary
{
    internal static ushort U16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
    internal static uint U32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
    internal static int I32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
    internal static ulong U64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
    internal static long I64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(source.Slice(offset, 8));

    internal static void U16(Span<byte> destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), value);
    internal static void U32(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
    internal static void I32(Span<byte> destination, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);
    internal static void U64(Span<byte> destination, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset, 8), value);
    internal static void I64(Span<byte> destination, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(offset, 8), value);

    internal static ServiceCycleReplayCycleKey ReadCycleKey(ReadOnlySpan<byte> source, int offset)
    {
        if (U32(source, offset + 4) != 0) throw Error(ServiceCycleReplayFormatErrorCode.ReservedBytesNonzero);
        return new ServiceCycleReplayCycleKey(
            I32(source, offset),
            U64(source, offset + 8),
            U64(source, offset + 16),
            U64(source, offset + 24),
            U64(source, offset + 32),
            U64(source, offset + 40));
    }

    internal static void WriteCycleKey(Span<byte> destination, int offset, in ServiceCycleReplayCycleKey key)
    {
        I32(destination, offset, key.TraceServiceKey);
        U32(destination, offset + 4, 0);
        U64(destination, offset + 8, key.Lifecycle);
        U64(destination, offset + 16, key.Configuration);
        U64(destination, offset + 24, key.Strategy);
        U64(destination, offset + 32, key.Capture);
        U64(destination, offset + 40, key.Cycle);
    }

    internal static bool IsZero(ReadOnlySpan<byte> source)
    {
        for (var index = 0; index < source.Length; index++)
            if (source[index] != 0) return false;
        return true;
    }

    internal static ServiceCycleReplayFormatException Error(
        ServiceCycleReplayFormatErrorCode code,
        int index = -1) => new(code, index);
}
internal static class ServiceCycleReplayCrc32
{
    private const uint Polynomial = 0xedb88320u;

    internal static uint Compute(ReadOnlySpan<byte> data) => ~Update(uint.MaxValue, data);

    internal static uint ComputeExcluding(ReadOnlySpan<byte> data, int offset, int length)
    {
        var crc = Update(uint.MaxValue, data.Slice(0, offset));
        crc = Update(crc, data.Slice(offset + length));
        return ~crc;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        for (var index = 0; index < data.Length; index++)
        {
            crc ^= data[index];
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (Polynomial & (uint)-(int)(crc & 1));
        }
        return crc;
    }
}

internal readonly struct ServiceCycleReplaySection
{
    internal ServiceCycleReplaySection(
        ServiceCycleReplaySectionKind kind,
        ushort version,
        int offset,
        int length,
        int count,
        uint checksum)
    {
        Kind = kind;
        Version = version;
        Offset = offset;
        Length = length;
        Count = count;
        Checksum = checksum;
    }

    internal ServiceCycleReplaySectionKind Kind { get; }
    internal ushort Version { get; }
    internal int Offset { get; }
    internal int Length { get; }
    internal int Count { get; }
    internal uint Checksum { get; }
}
