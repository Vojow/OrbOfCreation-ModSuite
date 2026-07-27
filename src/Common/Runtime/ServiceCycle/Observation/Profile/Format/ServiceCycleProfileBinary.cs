#if SERVICE_CYCLE_PROFILE
using System;
using System.Buffers.Binary;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;

internal static class ServiceCycleProfileBinary
{
    internal static ushort U16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    internal static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    internal static int I32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    internal static ulong U64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, 8));
    internal static long I64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));
    internal static void U16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), value);
    internal static void U32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(offset, 4), value);
    internal static void I32(Span<byte> bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(offset, 4), value);
    internal static void U64(Span<byte> bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(offset, 8), value);
    internal static void I64(Span<byte> bytes, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(offset, 8), value);

    internal static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
            if (bytes[index] != 0) return false;
        return true;
    }
}
#endif
