using System;

namespace OrbModding.Common.Runtime.Tracing;

internal static class TraceCrc32
{
    private const uint Polynomial = 0xedb88320u;

    internal static uint ComputeExcluding(ReadOnlySpan<byte> data, int skipOffset, int skipLength)
    {
        var crc = uint.MaxValue;
        crc = Update(crc, data.Slice(0, skipOffset));
        crc = Update(crc, data.Slice(skipOffset + skipLength));
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
