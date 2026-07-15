using System;

namespace OrbMentor;

internal readonly struct MentorAmount : IComparable<MentorAmount>, IEquatable<MentorAmount>
{
    public MentorAmount(double mantissa, long exponent)
    {
        if (!double.IsFinite(mantissa) || mantissa <= 0.0)
        {
            Mantissa = 0.0;
            Exponent = 0;
            return;
        }

        while (mantissa >= 10.0) { mantissa /= 10.0; exponent++; }
        while (mantissa < 1.0) { mantissa *= 10.0; exponent--; }
        Mantissa = mantissa;
        Exponent = exponent;
    }

    public double Mantissa { get; }
    public long Exponent { get; }
    public bool IsValidPositive => double.IsFinite(Mantissa) && Mantissa > 0.0;

    public MentorAmount Add(MentorAmount other)
    {
        if (!IsValidPositive) return other;
        if (!other.IsValidPositive) return this;
        var high = Exponent >= other.Exponent ? this : other;
        var low = Exponent >= other.Exponent ? other : this;
        var gap = high.Exponent - low.Exponent;
        return gap > 17 ? high : new MentorAmount(high.Mantissa + low.Mantissa * Math.Pow(10.0, -gap), high.Exponent);
    }

    public MentorAmount Multiply(double factor) =>
        !IsValidPositive || !double.IsFinite(factor) || factor <= 0.0
            ? default
            : new MentorAmount(Mantissa * factor, Exponent);

    public int CompareTo(MentorAmount other) => Exponent != other.Exponent
        ? Exponent.CompareTo(other.Exponent)
        : Mantissa.CompareTo(other.Mantissa);
    public bool Equals(MentorAmount other) => Mantissa.Equals(other.Mantissa) && Exponent == other.Exponent;
    public override bool Equals(object? obj) => obj is MentorAmount other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);
}
