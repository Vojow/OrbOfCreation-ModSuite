using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Canonical numeric fingerprint for a bounded semantic state projection.</summary>
public static class ServiceCycleProjectionFingerprint
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Compute(in ServiceStateProjectionSnapshot projection)
    {
        var hash = Mix(OffsetBasis, unchecked((ulong)projection.Count));
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            hash = Mix(hash, unchecked((ulong)entry.Key.Value));
            hash = Mix(hash, unchecked((ulong)entry.Value.Kind));
            hash = entry.Value.Kind == ServiceProjectionValueKind.FloatingPoint
                ? Mix(hash, unchecked((ulong)System.BitConverter.DoubleToInt64Bits(entry.Value.FloatingPoint)))
                : Mix(hash, unchecked((ulong)entry.Value.Integer));
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        for (var shift = 0; shift != 64; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= Prime;
        }

        return hash;
    }
}
