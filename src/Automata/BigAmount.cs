using System;
using System.Globalization;
using System.Reflection;

namespace OrbAutomata;

internal readonly struct BigAmount : IComparable<BigAmount>
{
    public BigAmount(double mantissa, long exponent)
    {
        if (mantissa == 0.0 || double.IsNaN(mantissa) || double.IsInfinity(mantissa))
        {
            Mantissa = 0.0;
            Exponent = 0;
            return;
        }

        var sign = Math.Sign(mantissa);
        mantissa = Math.Abs(mantissa);
        while (mantissa >= 10.0)
        {
            mantissa /= 10.0;
            exponent++;
        }

        while (mantissa < 1.0)
        {
            mantissa *= 10.0;
            exponent--;
        }

        Mantissa = mantissa * sign;
        Exponent = exponent;
    }

    public double Mantissa { get; }

    public long Exponent { get; }

    public bool IsZero => Mantissa == 0.0;

    public bool IsNegative => Mantissa < 0.0;

    public static bool TryParse(string value, out BigAmount amount)
    {
        amount = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        return TryFromDouble(parsed, out amount);
    }

    public static bool TryRead(object? value, out BigAmount amount)
    {
        amount = default;
        if (value is null)
        {
            return false;
        }

        switch (value)
        {
            case BigAmount existing:
                amount = existing;
                return true;
            case double doubleValue:
                return TryFromDouble(doubleValue, out amount);
            case float floatValue:
                return TryFromDouble(floatValue, out amount);
            case int intValue:
                amount = FromDouble(intValue);
                return true;
            case long longValue:
                amount = FromDouble(longValue);
                return true;
        }

        var type = value.GetType();
        if (!type.Name.Contains("BigDouble", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryReadDoubleMember(type, value, "mantissa", out var mantissa) &&
            !TryReadDoubleMember(type, value, "Ma", out mantissa) &&
            !TryReadDoubleMember(type, value, "Mantissa", out mantissa))
        {
            return false;
        }

        if (!TryReadLongMember(type, value, "exponent", out var exponent) &&
            !TryReadLongMember(type, value, "Ex", out exponent) &&
            !TryReadLongMember(type, value, "Exponent", out exponent))
        {
            return false;
        }

        amount = new BigAmount(mantissa, exponent);
        return true;
    }

    public BigAmount Add(BigAmount other)
    {
        if (IsZero)
        {
            return other;
        }

        if (other.IsZero)
        {
            return this;
        }

        var high = this;
        var low = other;
        if (other.Exponent > Exponent)
        {
            high = other;
            low = this;
        }

        var gap = high.Exponent - low.Exponent;
        if (gap > 18)
        {
            return high;
        }

        return new BigAmount(high.Mantissa + low.Mantissa * Math.Pow(10.0, -gap), high.Exponent);
    }

    public BigAmount Multiply(double factor)
    {
        if (IsZero || factor == 0.0 || double.IsNaN(factor) || double.IsInfinity(factor))
        {
            return default;
        }

        return new BigAmount(Mantissa * factor, Exponent);
    }

    public BigAmount Subtract(BigAmount other) => Add(other.Multiply(-1.0));

    public double DivideApprox(BigAmount denominator)
    {
        if (denominator.IsZero)
        {
            return double.PositiveInfinity;
        }

        var exponentGap = Exponent - denominator.Exponent;
        if (exponentGap > 308)
        {
            return double.PositiveInfinity;
        }

        if (exponentGap < -308)
        {
            return 0.0;
        }

        return Mantissa / denominator.Mantissa * Math.Pow(10.0, exponentGap);
    }

    public int CompareTo(BigAmount other)
    {
        if (IsZero && other.IsZero)
        {
            return 0;
        }

        if (Mantissa < 0.0 || other.Mantissa < 0.0)
        {
            return ToSignedLog().CompareTo(other.ToSignedLog());
        }

        var exponent = Exponent.CompareTo(other.Exponent);
        if (exponent != 0)
        {
            return exponent;
        }

        return Mantissa.CompareTo(other.Mantissa);
    }

    public override string ToString()
    {
        return IsZero
            ? "0"
            : $"{Mantissa.ToString("0.###", CultureInfo.InvariantCulture)}e{Exponent.ToString(CultureInfo.InvariantCulture)}";
    }

    public static BigAmount Max(BigAmount left, BigAmount right)
    {
        return left.CompareTo(right) >= 0 ? left : right;
    }

    private static BigAmount FromDouble(double value)
    {
        if (value == 0.0)
        {
            return default;
        }

        var exponent = (long)Math.Floor(Math.Log10(Math.Abs(value)));
        return new BigAmount(value / Math.Pow(10.0, exponent), exponent);
    }

    private static bool TryFromDouble(double value, out BigAmount amount)
    {
        amount = default;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        amount = FromDouble(value);
        return true;
    }

    private double ToSignedLog()
    {
        if (IsZero)
        {
            return double.NegativeInfinity;
        }

        return Math.Sign(Mantissa) * (Math.Log10(Math.Abs(Mantissa)) + Exponent);
    }

    private static bool TryReadDoubleMember(Type type, object instance, string name, out double value)
    {
        value = 0.0;
        var memberValue = ReadMember(type, instance, name);
        if (memberValue is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(memberValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadLongMember(Type type, object instance, string name, out long value)
    {
        value = 0;
        var memberValue = ReadMember(type, instance, name);
        if (memberValue is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt64(memberValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
        {
            return false;
        }
    }

    private static object? ReadMember(Type type, object instance, string name)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = type.GetField(name, Flags);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        var property = type.GetProperty(name, Flags);
        if (property is null || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}
