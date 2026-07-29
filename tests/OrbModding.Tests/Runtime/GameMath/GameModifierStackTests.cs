using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the modifier stack against the original's semantics. The merge-before-apply rule is the
/// part that produces plausible-but-wrong numbers when got wrong, so it is tested directly and
/// against the sequential result it must NOT equal.
/// </summary>
public sealed class GameModifierStackTests
{
    private static BigDouble Adjust(BigDouble baseValue, params GameValueModifier[] modifiers) =>
        GameModifierStack.AdjustWith(baseValue, modifiers);

    [Fact]
    public void AnEmptyStackReturnsTheBaseValue()
    {
        Assert.Equal(50d, GameModifierStack.AdjustWith(new BigDouble(50d), ReadOnlySpan<GameValueModifier>.Empty).ToDouble(), 9);
    }

    [Fact]
    public void SameOrderDiminishingModifiersMergeRatherThanCompound()
    {
        // The load-bearing case. Two +50% diminishing modifiers at the same order sum to +100%,
        // giving 100 * (1 + 0.5 + 0.5) = 200 — NOT 100 * 1.5 * 1.5 = 225.
        var result = Adjust(
            new BigDouble(100d),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d)),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d)));

        Assert.Equal(200d, result.ToDouble(), 9);
        Assert.NotEqual(225d, result.ToDouble(), 9);
    }

    [Fact]
    public void DifferentOrdersCompoundBecauseTheyAreSeparatePasses()
    {
        // The same two modifiers at different orders are two passes, so they DO compound:
        // 100 * 1.5 = 150, then 150 * 1.5 = 225.
        var result = Adjust(
            new BigDouble(100d),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d), order: 0),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d), order: 1));

        Assert.Equal(225d, result.ToDouble(), 9);
    }

    [Fact]
    public void StackingModifiersMultiplyWhenMerged()
    {
        // MultiStacking merges by multiplication and applies by multiplication: 10 * (2 * 3) = 60.
        var result = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(3d)));

        Assert.Equal(60d, result.ToDouble(), 9);
    }

    [Fact]
    public void TypesApplyInTheOriginalsFixedOrderWithinOneGroup()
    {
        // Raw before MultiStacking: (10 + 5) * 2 = 30. If the order flipped it would be 10*2+5 = 25,
        // so this pins the application order rather than merely that both were applied.
        var result = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(5d)));

        Assert.Equal(30d, result.ToDouble(), 9);
    }

    [Fact]
    public void LowerOrdersApplyFirstRegardlessOfInputSequence()
    {
        // Order 0 adds 5, order 1 doubles: (10 + 5) * 2 = 30. Supplied highest-first to prove the
        // stack sorts rather than trusting caller sequence.
        var result = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d), order: 1),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(5d), order: 0));

        Assert.Equal(30d, result.ToDouble(), 9);

        // Negative orders are legitimate (the game uses order -2 for base adjustments).
        var withNegative = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d), order: 0),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(5d), order: -2));
        Assert.Equal(30d, withNegative.ToDouble(), 9);
    }

    [Fact]
    public void ModifiersThatMergeBackToIdentityAreNotApplied()
    {
        // +0.5 and -0.5 diminishing cancel to identity; the original skips empty combined
        // modifiers, so the base value must survive untouched.
        var result = Adjust(
            new BigDouble(100d),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d)),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(-0.5d)));

        Assert.Equal(100d, result.ToDouble(), 9);
    }

    [Fact]
    public void ReductionDividesAndRawAdds()
    {
        Assert.Equal(50d, Adjust(new BigDouble(100d),
            new GameValueModifier(GameValueModifierType.Reduction, new BigDouble(1d))).ToDouble(), 9);

        Assert.Equal(105d, Adjust(new BigDouble(100d),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(5d))).ToDouble(), 9);
    }

    [Fact]
    public void ExponentInvertsItsPowerForValuesBetweenZeroAndOne()
    {
        // Above 1 the exponent applies directly: 4^2 = 16.
        Assert.Equal(16d, Adjust(new BigDouble(4d),
            new GameValueModifier(GameValueModifierType.Exponent, new BigDouble(2d))).ToDouble(), 6);

        // Strictly between 0 and 1 the original inverts the exponent, so 0.25^(1/2) = 0.5 rather
        // than 0.25^2 = 0.0625 — a modifier meant to increase does not shrink a fraction.
        Assert.Equal(0.5d, Adjust(new BigDouble(0.25d),
            new GameValueModifier(GameValueModifierType.Exponent, new BigDouble(2d))).ToDouble(), 6);
    }

    [Fact]
    public void MergingIsIndependentPerTypeWithinAGroup()
    {
        // Raw sums (2+3=5) while Stacking multiplies (2*3=6), in one order group:
        // (10 + 5) * 6 = 90.
        var result = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(3d)),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(3d)));

        Assert.Equal(90d, result.ToDouble(), 9);
    }

    /// <summary>
    /// One modifier of each type against the same base, so every arm of
    /// <c>ValueModifier.Adjust</c> is pinned individually rather than only in combination.
    /// </summary>
    /// <remarks>
    /// The type travels as its underlying integer because the enum is internal to the suite and a
    /// public test method may not name it — the same reason the collector publishes enums as ints.
    /// </remarks>
    [Theory]
    [InlineData(0, 3d, 103d)]      // Raw:              100 + 3
    [InlineData(1, 0.25d, 125d)]   // MultiDiminishing: 100 * (1 + 0.25)
    [InlineData(2, 3d, 300d)]      // MultiStacking:    100 * 3
    [InlineData(3, 3d, 25d)]       // Reduction:        100 / (1 + 3)
    [InlineData(4, 2d, 10000d)]    // Exponent:         100 ^ 2
    public void EachTypeAppliesItsOwnArithmetic(int type, double amount, double expected)
    {
        var modifier = new GameValueModifier((GameValueModifierType)type, new BigDouble(amount));

        Assert.Equal(expected, Adjust(new BigDouble(100d), modifier).ToDouble(), 6);
    }

    /// <summary>
    /// The whole five-stage sequence in one order group, in the order the original appends it:
    /// Raw, then MultiDiminishing, then MultiStacking, then Reduction, then Exponent.
    /// </summary>
    /// <remarks>
    /// Not commutative, and that is the point. (10 + 6) = 16; ×(1 + 0.5) = 24; ×2 = 48; ÷(1 + 1) =
    /// 24; ^2 = 576. Reordering Reduction and Exponent alone gives 48² / 2 = 1152, which is a
    /// perfectly plausible number and wrong.
    /// </remarks>
    [Fact]
    public void TheFiveStagesApplyInTheOriginalsSequence()
    {
        var result = Adjust(
            new BigDouble(10d),
            new GameValueModifier(GameValueModifierType.Exponent, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.Reduction, new BigDouble(1d)),
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
            new GameValueModifier(GameValueModifierType.MultiDiminishing, new BigDouble(0.5d)),
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(6d)));

        Assert.Equal(576d, result.ToDouble(), 6);
        Assert.NotEqual(1152d, result.ToDouble(), 6);
    }

    /// <summary>
    /// The magnitudes this save actually runs at. A fold that went through <c>double</c> anywhere
    /// would overflow to infinity here, which is exactly what reading the modifier's lossy public
    /// <c>adjust</c> field instead of <c>adjustReal</c> would do.
    /// </summary>
    [Fact]
    public void MagnitudesBeyondDoubleRangeSurviveTheFold()
    {
        var huge = BigDouble.Pow10(300L);

        var raw = Adjust(huge, new GameValueModifier(GameValueModifierType.Raw, huge));
        Assert.Equal(300L, raw.Exponent);
        Assert.Equal(2d, raw.Mantissa, 6);

        var stacked = Adjust(huge, new GameValueModifier(GameValueModifierType.MultiStacking, huge));
        Assert.Equal(600L, stacked.Exponent);
        Assert.Equal(1d, stacked.Mantissa, 6);
    }

    [Fact]
    public void AnEmptyStackIsTheIdentityAtEveryMagnitude()
    {
        var huge = BigDouble.Pow10(400L);

        Assert.Equal(
            huge,
            GameModifierStack.AdjustWith(huge, ReadOnlySpan<GameValueModifier>.Empty));
        Assert.Equal(
            BigDouble.Zero,
            GameModifierStack.AdjustWith(BigDouble.Zero, ReadOnlySpan<GameValueModifier>.Empty));
    }
}
