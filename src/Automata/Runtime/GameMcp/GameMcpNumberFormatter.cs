#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;

namespace OrbAutomata.GameMcp;

/// <summary>Canonical compact wire representation for game-domain large numbers.</summary>
internal static class GameMcpNumberFormatter
{
    internal static string Format(object value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value is not BigDouble number)
            throw new ArgumentException("Only BigDouble values use the MCP large-number formatter.", nameof(value));
        return Format(number.Mantissa, number.Exponent);
    }

    internal static string Format(double mantissa, long exponent)
    {
        if (double.IsNaN(mantissa)) return "nan";
        if (double.IsPositiveInfinity(mantissa)) return "infinity";
        if (double.IsNegativeInfinity(mantissa)) return "-infinity";
        if (mantissa == 0d) return "0";

        Normalize(ref mantissa, ref exponent);
        return Scientific(mantissa, exponent);
    }

    private static string Scientific(double mantissa, long exponent)
    {
        var rounded = Math.Round(mantissa, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) >= 10d)
        {
            rounded /= 10d;
            checked { exponent++; }
        }
        if (rounded == 0d) return "0";
        return rounded.ToString("0.##", CultureInfo.InvariantCulture) +
            "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static void Normalize(ref double mantissa, ref long exponent)
    {
        var shift = (long)Math.Floor(Math.Log10(Math.Abs(mantissa)));
        if (shift == 0) return;
        mantissa /= Math.Pow(10d, shift);
        checked { exponent += shift; }
    }
}
#endif
