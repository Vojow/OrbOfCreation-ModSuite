using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// Pins the ported purchase-cost chain. These assert the sequence and arithmetic of
/// <c>StructureSO.GetNextCost()</c>, not merely that a number comes out.
/// </summary>
public sealed class GameCostMathTests
{
    private static readonly Guid Water = new("eab888ff-d8bd-4e46-81eb-639d5d562242");
    private static readonly Guid Mana = new("b11072bf-7980-4e23-bc6c-8034ba09b925");

    private static BigDouble One => BigDouble.One;

    [Fact]
    public void EachEntryScalesByItsOwnResourcesAttributeModifier()
    {
        // AdjustAsAttribute is per-resource, not per-cost-list: two entries in one cost can scale
        // by different amounts, which is why the modifiers are supplied positionally.
        Span<GameResourceCost> costs = stackalloc GameResourceCost[]
        {
            new GameResourceCost(Water, new BigDouble(100d)),
            new GameResourceCost(Mana, new BigDouble(100d)),
        };
        ReadOnlySpan<BigDouble> mods = stackalloc BigDouble[] { new BigDouble(2d), new BigDouble(0.5d) };

        GameCostMath.AdjustAsAttribute(costs, mods);

        Assert.Equal(200d, costs[0].Value.ToDouble(), 9);
        Assert.Equal(50d, costs[1].Value.ToDouble(), 9);

        // Resource identity survives the transform; only the magnitude is replaced.
        Assert.Equal(Water, costs[0].ResourceId);
        Assert.Equal(Mana, costs[1].ResourceId);
    }

    [Fact]
    public void AMismatchedModifierCountIsRejectedRatherThanSilentlyMisaligned()
    {
        // Positional alignment is load-bearing: a short list would otherwise scale the wrong
        // resource by the wrong modifier and still produce plausible numbers.
        Assert.Throws<ArgumentException>(() =>
        {
            var costs = new GameResourceCost[2];
            var mods = new BigDouble[1];
            GameCostMath.AdjustAsAttribute(costs, mods);
        });
    }

    [Fact]
    public void MultiplyAndRoundApplyToEveryEntry()
    {
        Span<GameResourceCost> costs = stackalloc GameResourceCost[]
        {
            new GameResourceCost(Water, new BigDouble(12.34d)),
            new GameResourceCost(Mana, new BigDouble(1234d)),
        };

        GameCostMath.Multiply(costs, new BigDouble(2d));
        Assert.Equal(24.68d, costs[0].Value.ToDouble(), 6);
        Assert.Equal(2468d, costs[1].Value.ToDouble(), 6);

        GameCostMath.RoundToTwoSigsEarly(costs);

        // 24.68 is inside [10,100) so it snaps; 2468 is above 100 so it passes through.
        Assert.Equal(25d, costs[0].Value.ToDouble(), 6);
        Assert.Equal(2468d, costs[1].Value.ToDouble(), 6);
    }

    [Fact]
    public void NextCostAppliesTheOriginalsSequence()
    {
        // Hand-computed against the original's order:
        //   base 50, attribute mod 100% => 50
        //   per-quantity Raw(+10) scaled by 100% then by 3 committed levels => +30 => 80
        //   next-cost mod 200% => 160
        //   RoundToTwoSigsEarly(160) => 160 (>= 100 passes through)
        Span<GameResourceCost> costs = stackalloc GameResourceCost[]
        {
            new GameResourceCost(Water, new BigDouble(50d)),
        };
        ReadOnlySpan<BigDouble> attributeMods = stackalloc BigDouble[] { One };

        GameCostMath.ComputeNextCost(
            costs,
            attributeMods,
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(10d)),
            costScalingModPercent: One,
            committedQuantity: new BigDouble(3d),
            nextCostModPercent: new BigDouble(2d));

        Assert.Equal(160d, costs[0].Value.ToDouble(), 6);
    }

    [Fact]
    public void QueuedLevelsRaiseTheNextCost()
    {
        // The committed quantity is quantity + queuedQuantity. Pricing on owned levels alone would
        // under-price every structure with work already in flight, so this pins that more committed
        // levels cost strictly more.
        static BigDouble CostAt(double committed)
        {
            Span<GameResourceCost> costs = stackalloc GameResourceCost[]
            {
                new GameResourceCost(Water, new BigDouble(1000d)),
            };
            ReadOnlySpan<BigDouble> mods = stackalloc BigDouble[] { BigDouble.One };

            GameCostMath.ComputeNextCost(
                costs,
                mods,
                new GameValueModifier(GameValueModifierType.Raw, new BigDouble(100d)),
                costScalingModPercent: BigDouble.One,
                committedQuantity: new BigDouble(committed),
                nextCostModPercent: BigDouble.One);

            return costs[0].Value;
        }

        Assert.Equal(1000d, CostAt(0).ToDouble(), 6);
        Assert.Equal(1500d, CostAt(5).ToDouble(), 6);
        Assert.True(CostAt(8) > CostAt(5));
    }

    [Fact]
    public void AStackingPerQuantityModifierCompoundsWithCommittedLevels()
    {
        // MultiplyScalar exponentiates multiplicative kinds, so a x2 per-level stacking modifier at
        // 3 levels is x8 rather than x6. This is the case a linear port would get wrong.
        Span<GameResourceCost> costs = stackalloc GameResourceCost[]
        {
            new GameResourceCost(Water, new BigDouble(10d)),
        };
        ReadOnlySpan<BigDouble> mods = stackalloc BigDouble[] { BigDouble.One };

        GameCostMath.ComputeNextCost(
            costs,
            mods,
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
            costScalingModPercent: BigDouble.One,
            committedQuantity: new BigDouble(3d),
            nextCostModPercent: BigDouble.One);

        Assert.Equal(80d, costs[0].Value.ToDouble(), 6);
    }

    /// <summary>
    /// The next-cost multiplier, derived by hand from the original rather than from the port.
    /// </summary>
    /// <remarks>
    /// With a Raw per-quantity modifier of 0.25 at four committed levels,
    /// <c>MultiplyScalar(4).Adjust(1)</c> is <c>1 + (0.25 × 4) = 2</c>, so the reciprocal term is
    /// <c>100 / 2 = 50</c>. The passive modifier of 120 is larger, so the <c>Max</c> takes it. The
    /// active side is <c>(150 × 1.2) = 180</c>, and <c>AsPercent</c> makes it 1.8 — giving
    /// <c>120 × 1.8 = 216</c>.
    /// </remarks>
    [Fact]
    public void TheNextCostModifierFloorsTheReciprocalTermAtThePassiveModifier()
    {
        var perQuantity = new GameValueModifier(GameValueModifierType.Raw, new BigDouble(0.25d));

        var mod = GameCostMath.ComputeNextCostMod(
            passiveCostMod: new BigDouble(120d),
            activeCostMod: new BigDouble(150d),
            in perQuantity,
            nextQuantity: new BigDouble(4d),
            structureCostPercent: new BigDouble(1.2d));

        Assert.Equal(216d, mod.ToDouble(), 6);
    }

    /// <summary>
    /// And the other side of the <c>Max</c>: a weak per-quantity modifier leaves the reciprocal term
    /// above the passive floor, so scaling rather than the floor sets the price.
    /// </summary>
    /// <remarks>
    /// <c>1 + (0.05 × 2) = 1.1</c>, so the term is <c>100 / 1.1 ≈ 90.909</c> against a passive
    /// modifier of 80. The active side is one, so the result is the term itself.
    /// </remarks>
    [Fact]
    public void AWeakPerQuantityModifierLeavesTheReciprocalTermInCharge()
    {
        var perQuantity = new GameValueModifier(GameValueModifierType.Raw, new BigDouble(0.05d));

        var mod = GameCostMath.ComputeNextCostMod(
            passiveCostMod: new BigDouble(80d),
            activeCostMod: new BigDouble(100d),
            in perQuantity,
            nextQuantity: new BigDouble(2d),
            structureCostPercent: BigDouble.One);

        Assert.Equal(100d / 1.1d, mod.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade with nothing bought and nothing queued pays its authored cost, only rounded.
    /// </summary>
    /// <remarks>
    /// The <c>n == 1</c> branch of <c>SetToLevel</c>, and the easiest one to lose: the level passed in
    /// is one past the committed level, so an untouched upgrade arrives here rather than at the
    /// scaling path.
    /// </remarks>
    [Fact]
    public void AnUntouchedUpgradePaysItsAuthoredCost()
    {
        Span<GameResourceCost> costs = stackalloc[] { new GameResourceCost(Water, new BigDouble(100d)) };
        Span<GameValueModifier> perLevel = stackalloc[]
        {
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
        };
        Span<GameValueModifier> scratch = stackalloc GameValueModifier[1];

        GameCostMath.ComputeLeveledCost(
            costs, perLevel, ReadOnlySpan<GameValueModifier>.Empty, level: 1,
            stackalloc GameValueModifier[1], Span<GameValueModifier>.Empty, scratch);

        Assert.Equal(100d, costs[0].Value.ToDouble(), 6);
    }

    /// <summary>
    /// The per-level modifier is scaled by the level minus one, and a stacking modifier scales by
    /// exponentiation rather than multiplication.
    /// </summary>
    /// <remarks>
    /// At level 3 the scalar is 2, so <c>Stacking 2</c> becomes <c>Stacking 2² = 4</c> and the cost
    /// quadruples. Scaling it linearly would double it instead — the same distinction
    /// <see cref="GameValueModifier.MultiplyScalar"/> draws, here where it decides a price.
    /// </remarks>
    [Fact]
    public void AStackingPerLevelModifierScalesByExponentiation()
    {
        Span<GameResourceCost> costs = stackalloc[] { new GameResourceCost(Water, new BigDouble(100d)) };
        Span<GameValueModifier> perLevel = stackalloc[]
        {
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
        };

        GameCostMath.ComputeLeveledCost(
            costs, perLevel, ReadOnlySpan<GameValueModifier>.Empty, level: 3,
            stackalloc GameValueModifier[1], Span<GameValueModifier>.Empty,
            stackalloc GameValueModifier[1]);

        Assert.Equal(400d, costs[0].Value.ToDouble(), 6);
    }

    /// <summary>
    /// The upgrade chain rounds with <c>RoundToTwoSigs</c>, not the <c>…Early</c> variant the
    /// structure chain ends with.
    /// </summary>
    /// <remarks>
    /// 225 is the case that tells them apart: <c>…Early</c> passes anything at or above 100 through
    /// untouched, while this one snaps to two significant digits at every magnitude — and does it with
    /// round-half-to-even, so 22.5 goes to 22 and the price lands on 220 rather than 230.
    /// </remarks>
    [Fact]
    public void AnUpgradePriceIsSnappedToTwoSignificantDigitsAtEveryMagnitude()
    {
        Span<GameResourceCost> costs = stackalloc[] { new GameResourceCost(Water, new BigDouble(100d)) };
        Span<GameValueModifier> perLevel = stackalloc[]
        {
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(1.5d)),
        };

        GameCostMath.ComputeLeveledCost(
            costs, perLevel, ReadOnlySpan<GameValueModifier>.Empty, level: 3,
            stackalloc GameValueModifier[1], Span<GameValueModifier>.Empty,
            stackalloc GameValueModifier[1]);

        Assert.Equal(220d, costs[0].Value.ToDouble(), 6);
    }

    /// <summary>
    /// An exponent list strengthens the per-level modifiers before any of them touches the cost.
    /// </summary>
    /// <remarks>
    /// <c>Raw 1</c> applied to <c>Stacking 2</c> raises it to the power of what the raw modifier does
    /// to one — <c>2¹⁺¹ = 4</c> — so the cost quadruples at level 2 where it would otherwise double.
    /// Applying the two lists in sequence instead would give <c>100 × 2 = 200</c> and then add one.
    /// </remarks>
    [Fact]
    public void AnExponentListStrengthensThePerLevelModifiersFirst()
    {
        Span<GameResourceCost> costs = stackalloc[] { new GameResourceCost(Water, new BigDouble(100d)) };
        Span<GameValueModifier> perLevel = stackalloc[]
        {
            new GameValueModifier(GameValueModifierType.MultiStacking, new BigDouble(2d)),
        };
        Span<GameValueModifier> exponents = stackalloc[]
        {
            new GameValueModifier(GameValueModifierType.Raw, BigDouble.One),
        };

        GameCostMath.ComputeLeveledCost(
            costs, perLevel, exponents, level: 2,
            stackalloc GameValueModifier[1], stackalloc GameValueModifier[1],
            stackalloc GameValueModifier[1]);

        Assert.Equal(400d, costs[0].Value.ToDouble(), 6);
    }

    [Fact]
    public void AnEmptyCostIsHandledWithoutSpecialCasing()
    {
        // Some purchases are free. The chain must be a no-op rather than a failure.
        GameCostMath.ComputeNextCost(
            Span<GameResourceCost>.Empty,
            ReadOnlySpan<BigDouble>.Empty,
            new GameValueModifier(GameValueModifierType.Raw, new BigDouble(10d)),
            costScalingModPercent: BigDouble.One,
            committedQuantity: new BigDouble(3d),
            nextCostModPercent: BigDouble.One);
    }
}
