using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

public sealed class GameResourceSpendMathTests
{
    [Fact]
    public void TrueSpendDividesByQualityPercent()
    {
        Assert.True(GameResourceSpendMath.TryGetTrueSpend(
            amount: 10d,
            quality: 200d,
            out var spend));

        Assert.Equal(5d, spend.ToDouble(), 12);
    }

    [Fact]
    public void ScaledDrainAppliesDrainPercentBeforeQuality()
    {
        Assert.True(GameResourceSpendMath.TryGetScaledDrain(
            baseCost: 10d,
            drainCostModifier: 150d,
            quality: 200d,
            out var drain));

        Assert.Equal(7.5d, drain.ToDouble(), 12);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-100d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidQuality(double quality)
    {
        Assert.False(GameResourceSpendMath.TryGetTrueSpend(
            amount: 10d,
            quality: new BigDouble(quality),
            out _));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidCostOrScaling(double invalid)
    {
        Assert.False(GameResourceSpendMath.TryGetScaledDrain(
            baseCost: new BigDouble(invalid),
            drainCostModifier: 100d,
            quality: 100d,
            out _));
        Assert.False(GameResourceSpendMath.TryGetScaledDrain(
            baseCost: 1d,
            drainCostModifier: new BigDouble(invalid),
            quality: 100d,
            out _));
    }

    [Fact]
    public void AcceptsExactZeroCost()
    {
        Assert.True(GameResourceSpendMath.TryGetScaledDrain(
            baseCost: BigDouble.Zero,
            drainCostModifier: 100d,
            quality: 100d,
            out var drain));

        Assert.Equal(BigDouble.Zero, drain);
    }

    [Fact]
    public void PreservesHugeFiniteMagnitude()
    {
        Assert.True(GameResourceSpendMath.TryGetScaledDrain(
            baseCost: new BigDouble(1d, 4096),
            drainCostModifier: 250d,
            quality: 125d,
            out var drain));

        Assert.Equal(new BigDouble(2d, 4096), drain);
    }
}
