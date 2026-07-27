using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the ported threshold arithmetic against the game's own self-tests.
/// </summary>
/// <remarks>
/// <para>
/// The expected values here are not derived — they are the assertions the game makes about itself, in
/// <c>TestRequirementLeveledValue</c>, <c>TestSumSequenceAdjust</c> and <c>TestSequenceAdjust</c>. That
/// matters more than usual for this port: a threshold is what decides whether a requirement is met,
/// and hand-deriving the expected value from the same reading of the source that produced the port
/// would let a misreading pass twice.
/// </para>
/// <para>
/// The scalar helpers are exercised through <see cref="GameLeveledValue"/> where the game's own tests
/// do, so what is under test is the sequence the port composes rather than three pieces that each look
/// plausible alone.
/// </para>
/// </remarks>
public sealed class GameLeveledValueTests
{
    private static GameValueModifier Raw(double amount) =>
        new(GameValueModifierType.Raw, new BigDouble(amount));

    /// <summary>The game's factory adds one, so <c>Stacking(1)</c> is a doubling.</summary>
    private static GameValueModifier Stacking(double amount) =>
        new(GameValueModifierType.MultiStacking, new BigDouble(amount + 1d));

    /// <summary>The game's <c>ValueModifier.empty</c>, which is a <c>Raw</c> of nought.</summary>
    private static GameValueModifier Empty =>
        new(GameValueModifierType.Raw, BigDouble.Zero);

    private static GameValueModifier Reduction =>
        new(GameValueModifierType.Reduction, new BigDouble(2d));

    private static long AtLevel(
        double baseValue,
        GameValueModifier perLevel,
        GameValueModifier modPerLevel,
        long level)
    {
        Assert.True(GameLeveledValue.TryAtLevel(
            new BigDouble(baseValue), perLevel, modPerLevel, level, out var value));
        return value.ToLong();
    }

    /// <summary>
    /// The game's <c>TestRequirementLeveledValue</c>, first case: an additive step that itself grows.
    /// </summary>
    [Fact]
    public void AThresholdWhoseStepGrowsFollowsTheGamesOwnSequence()
    {
        Assert.Equal(3L, AtLevel(3d, Raw(4), Raw(1), 0));
        Assert.Equal(7L, AtLevel(3d, Raw(4), Raw(1), 1));
        Assert.Equal(12L, AtLevel(3d, Raw(4), Raw(1), 2));
        Assert.Equal(18L, AtLevel(3d, Raw(4), Raw(1), 3));
        Assert.Equal(25L, AtLevel(3d, Raw(4), Raw(1), 4));
    }

    /// <summary>
    /// The same test's second case, where the step doubles each level rather than growing by a
    /// constant. This is the one that proves the sum switches on the target's type: reading the
    /// summing modifier's type instead would still produce a rising sequence, and none of these
    /// numbers.
    /// </summary>
    [Fact]
    public void AThresholdWhoseStepDoublesFollowsTheGamesOwnSequence()
    {
        Assert.Equal(5L, AtLevel(5d, Raw(2), Stacking(1), 0));
        Assert.Equal(7L, AtLevel(5d, Raw(2), Stacking(1), 1));
        Assert.Equal(11L, AtLevel(5d, Raw(2), Stacking(1), 2));
        Assert.Equal(19L, AtLevel(5d, Raw(2), Stacking(1), 3));
        Assert.Equal(35L, AtLevel(5d, Raw(2), Stacking(1), 4));
    }

    /// <summary>
    /// The same test's third case. A <c>Raw(0)</c> second modifier is empty, which takes the middle
    /// branch and makes the step flat.
    /// </summary>
    [Fact]
    public void AThresholdWithNoGrowthOnItsStepRisesFlat()
    {
        Assert.Equal(2L, AtLevel(2d, Raw(2), Raw(0), 0));
        Assert.Equal(4L, AtLevel(2d, Raw(2), Raw(0), 1));
        Assert.Equal(6L, AtLevel(2d, Raw(2), Raw(0), 2));
        Assert.Equal(8L, AtLevel(2d, Raw(2), Raw(0), 3));
    }

    /// <summary>
    /// A condition with no scaling at all is its base value at every level — the overwhelmingly common
    /// authored shape, and the shape of the live case this whole model was built for.
    /// </summary>
    [Fact]
    public void AThresholdWithNoScalingIsItsBaseValueAtEveryLevel()
    {
        Assert.Equal(6L, AtLevel(6d, Empty, Empty, 0));
        Assert.Equal(6L, AtLevel(6d, Empty, Empty, 1));
        Assert.Equal(6L, AtLevel(6d, Empty, Empty, 9));
    }

    /// <summary>
    /// A level at or below nought is the base value rather than an extrapolation backwards. The
    /// original's guard is <c>&lt;= 0</c>, and a structure at quantity nought reaches it.
    /// </summary>
    [Fact]
    public void ALevelAtOrBelowNoughtIsTheBaseValue()
    {
        Assert.Equal(3L, AtLevel(3d, Raw(4), Raw(1), 0));
        Assert.Equal(3L, AtLevel(3d, Raw(4), Raw(1), -5));
    }

    /// <summary>The game's <c>TestSumSequenceAdjust</c>, all three modifier shapes.</summary>
    [Fact]
    public void TheSequenceSumMatchesTheGamesOwnAssertions()
    {
        static long Sum(GameValueModifier modifier, double n)
        {
            Assert.True(GameLeveledValue.TrySumSequence(
                modifier, BigDouble.One, new BigDouble(n), out var sum));
            return sum.ToLong();
        }

        Assert.Equal(0L, Sum(Raw(1), -1));
        Assert.Equal(1L, Sum(Raw(1), 0));
        Assert.Equal(3L, Sum(Raw(1), 1));
        Assert.Equal(6L, Sum(Raw(1), 2));
        Assert.Equal(10L, Sum(Raw(1), 3));

        Assert.Equal(0L, Sum(Stacking(1), -1));
        Assert.Equal(1L, Sum(Stacking(1), 0));
        Assert.Equal(3L, Sum(Stacking(1), 1));
        Assert.Equal(7L, Sum(Stacking(1), 2));
        Assert.Equal(15L, Sum(Stacking(1), 3));

        Assert.Equal(0L, Sum(Empty, -1));
        Assert.Equal(1L, Sum(Empty, 0));
        Assert.Equal(2L, Sum(Empty, 1));
        Assert.Equal(3L, Sum(Empty, 2));
        Assert.Equal(4L, Sum(Empty, 3));
    }

    /// <summary>
    /// A multiplicative target is raised to the power of the summed unit series rather than having its
    /// amount summed. The exponent is the game's own asserted <c>SumSequenceAdjust(1, 3) == 15</c>, so
    /// a doubling target lands on two to the fifteenth and nowhere near a sum.
    /// </summary>
    [Fact]
    public void AMultiplicativeTargetIsRaisedRatherThanSummed()
    {
        Assert.True(GameLeveledValue.TrySumSequenceAdjust(
            Stacking(1), Stacking(1), new BigDouble(3d), out var result));

        Assert.Equal(GameValueModifierType.MultiStacking, result.Type);
        Assert.Equal(32768L, result.Amount.ToLong());
    }

    /// <summary>
    /// The two shapes the original refuses. It throws; the port answers false, because a threshold that
    /// cannot be computed has to make its condition unevaluable rather than take down the cycle that
    /// asked for it.
    /// </summary>
    [Fact]
    public void AScalingWithNoClosedFormIsRefusedRatherThanGuessed()
    {
        var exponent = new GameValueModifier(GameValueModifierType.Exponent, new BigDouble(2d));

        Assert.False(GameLeveledValue.TrySumSequence(Reduction, BigDouble.One, new BigDouble(3d), out _));
        Assert.False(GameLeveledValue.TrySumSequence(exponent, BigDouble.One, new BigDouble(3d), out _));
        Assert.False(GameLeveledValue.TryAtLevel(new BigDouble(3d), Raw(4), Reduction, 4, out _));
    }

    /// <summary>
    /// A refusal only reaches the caller where the level actually needs the sum. Below level one, and
    /// wherever the second modifier is empty, the unimplemented branch is never entered — so a
    /// condition carrying one of these still answers for the levels it can.
    /// </summary>
    [Fact]
    public void AnUncomputableScalingStillAnswersWhereTheSumIsNotNeeded()
    {
        Assert.Equal(3L, AtLevel(3d, Raw(4), Reduction, 0));

        // perLevel is the reduction here, and the middle branch only ever applies it: three divided by
        // one plus two.
        Assert.Equal(1L, AtLevel(3d, Reduction, Empty, 1));
    }
}
