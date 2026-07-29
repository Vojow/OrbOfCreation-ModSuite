using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the ported scalar arithmetic against the behaviour of the original it was transcribed
/// from. These are not "does the maths look right" tests — they are transcription-fidelity tests,
/// so they deliberately assert the original's quirks rather than the intuitive result.
/// </summary>
public sealed class OrbGameMathTests
{
    [Fact]
    public void AsPercentDividesByAHundredThroughTheExponent()
    {
        Assert.Equal(1d, OrbGameMath.AsPercent(new BigDouble(100d)).ToDouble(), 12);
        Assert.Equal(1.5d, OrbGameMath.AsPercent(new BigDouble(150d)).ToDouble(), 12);
        Assert.Equal(0d, OrbGameMath.AsPercent(BigDouble.Zero).ToDouble());

        // Magnitudes far past double range still divide exactly, which is the point of doing this
        // on the exponent rather than as a floating-point divide.
        var huge = OrbGameMath.AsPercent(new BigDouble(1d, 300));
        Assert.Equal(1d, huge.Mantissa, 12);
        Assert.Equal(298, huge.Exponent);
    }

    [Fact]
    public void RoundToTwoSigsKeepsTwoSignificantDigits()
    {
        Assert.Equal(1200d, OrbGameMath.RoundToTwoSigs(new BigDouble(1234d)).ToDouble(), 6);
        Assert.Equal(1200d, OrbGameMath.RoundToTwoSigs(new BigDouble(1249d)).ToDouble(), 6);

        // Fewer than two digits is returned untouched, per the original's early return.
        Assert.Equal(7d, OrbGameMath.RoundToTwoSigs(new BigDouble(7d)).ToDouble(), 6);
    }

    [Fact]
    public void RoundToTwoSigsUsesBankersRoundingAtTheMidpoint()
    {
        // The original delegates to BigDouble.Round, which is Math.Round(double) — round-half-to-
        // even, not away-from-zero. So 1250 scales to 12.5 and rounds DOWN to 12 => 1200, while
        // 1350 scales to 13.5 and rounds UP to 14 => 1400. Asserting both directions pins that the
        // midpoint rule is the .NET default rather than the intuitive one.
        Assert.Equal(1200d, OrbGameMath.RoundToTwoSigs(new BigDouble(1250d)).ToDouble(), 6);
        Assert.Equal(1400d, OrbGameMath.RoundToTwoSigs(new BigDouble(1350d)).ToDouble(), 6);
    }

    [Fact]
    public void RoundToTwoSigsEarlyOnlyAltersValuesBetweenTenAndAHundred()
    {
        // Two guards narrow this far more than the name suggests:
        //   - at 100 and above, the `>= 100` check returns the value untouched;
        //   - below 10, GetNumDigits (Exponent + 1) is under 2 and RoundToTwoSigs returns early.
        // So the only values this function actually changes are those in [10, 100).
        Assert.Equal(1234d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(1234d)).ToDouble(), 6);
        Assert.Equal(100d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(100d)).ToDouble(), 6);
        Assert.Equal(1.234d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(1.234d)).ToDouble(), 6);
        Assert.Equal(9.87d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(9.87d)).ToDouble(), 6);

        // Inside the window it snaps to a whole number.
        Assert.Equal(12d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(12.34d)).ToDouble(), 6);
        Assert.Equal(99d, OrbGameMath.RoundToTwoSigsEarly(new BigDouble(98.7d)).ToDouble(), 6);
    }

    [Fact]
    public void RoundToTwoSigsEarlyTakesTheRoundingBranchForNaN()
    {
        // The original is written `if (!(value >= 100))`, not `if (value < 100)`. For NaN the
        // comparison is false, so the negation sends NaN down the rounding path. This test exists
        // so that "simplifying" the condition later fails loudly instead of silently diverging.
        var result = OrbGameMath.RoundToTwoSigsEarly(BigDouble.NaN);
        Assert.True(BigDouble.IsNaN(result));
    }

    [Fact]
    public void RelativeErrorIsMeasuredAgainstTheSecondOperand()
    {
        // Not a symmetric relation, and the original does not pretend otherwise: the denominator is
        // the right-hand operand unless it is zero. Swapping the arguments can change the answer, so
        // argument order is part of the contract rather than a style choice.
        Assert.True(OrbGameMath.IsWithinError(new BigDouble(1d), new BigDouble(2d), 0.6d));
        Assert.False(OrbGameMath.IsWithinError(new BigDouble(2d), new BigDouble(1d), 0.6d));
    }

    [Fact]
    public void TwoZeroesAgreeRatherThanDividingByZero()
    {
        Assert.True(OrbGameMath.IsWithinError(BigDouble.Zero, BigDouble.Zero, 0.001d));

        // With only the right operand zero, the left one becomes the denominator.
        Assert.False(OrbGameMath.IsWithinError(new BigDouble(5d), BigDouble.Zero, 0.001d));
    }

    [Fact]
    public void ApproxErrorIsOnePartInAThousand()
    {
        // The window that keeps a resource sitting exactly at its cap from flickering into the
        // overflow branch on rounding noise.
        Assert.True(OrbGameMath.ApproxError(new BigDouble(1000.5d), new BigDouble(1000d)));
        Assert.False(OrbGameMath.ApproxError(new BigDouble(1002d), new BigDouble(1000d)));
    }

    [Fact]
    public void AGeometricSumCountsTermsFromZero()
    {
        // The parameter is n, and the series has n + 1 terms — an off-by-one waiting to happen if it
        // were reimplemented from the name alone. 2 + 1 + 0.5 = 3.5.
        Assert.Equal(3.5d, OrbGameMath.SumGeometricSequence(new BigDouble(2d), 0.5d, new BigDouble(2d)).ToDouble(), 10);

        // A ratio of one degenerates to plain multiplication rather than dividing by zero.
        Assert.Equal(15d, OrbGameMath.SumGeometricSequence(new BigDouble(3d), 1d, new BigDouble(4d)).ToDouble(), 10);
    }

    [Fact]
    public void FindingTheTermCountInvertsTheSum()
    {
        Assert.Equal(
            2d,
            OrbGameMath.FindGeometricSequenceN(new BigDouble(3.5d), new BigDouble(2d), 0.5d).ToDouble(),
            10);
    }

    [Fact]
    public void FindingTheTermCountRefusesARatioOfOne()
    {
        // The closed form divides by 1 - r. The original throws rather than returning infinity, and
        // the port keeps that so a caller reaching it is a bug report rather than a silent NaN.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OrbGameMath.FindGeometricSequenceN(new BigDouble(10d), new BigDouble(2d), 1d));
    }
}
