using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

public sealed class GameHarvestActionScalingMathTests
{
    [Fact]
    public void CombinesActionElementAndFullSpeedInNativeOrder()
    {
        var scratch = new GameValueModifier[1];

        Assert.True(TryScale(
            level: 1,
            hasInstanceScaling: true,
            cost: System.Array.Empty<GameValueModifier>(),
            speed: System.Array.Empty<GameValueModifier>(),
            scratch,
            out var modifier));

        Assert.Equal(180d, modifier.ToDouble(), 12);
    }

    [Fact]
    public void AppliesAuthoredCostAndSpeedListsAtLevel()
    {
        var cost = new[]
        {
            new GameValueModifier(GameValueModifierType.Raw, 0.25d),
        };
        var speed = new[]
        {
            new GameValueModifier(GameValueModifierType.MultiDiminishing, 0.1d),
        };
        var scratch = new GameValueModifier[1];

        Assert.True(TryScale(
            level: 3,
            hasInstanceScaling: true,
            cost,
            speed,
            scratch,
            out var modifier));

        // Base drain 180%; cost x1.5; final speed 120%, so 180 * 1.5 * 1.2.
        Assert.Equal(324d, modifier.ToDouble(), 12);
    }

    [Fact]
    public void MissingInstanceScalingUsesNativeLevelFallback()
    {
        var scratch = System.Array.Empty<GameValueModifier>();

        Assert.True(TryScale(
            level: 3,
            hasInstanceScaling: false,
            System.Array.Empty<GameValueModifier>(),
            System.Array.Empty<GameValueModifier>(),
            scratch,
            out var modifier));

        // Both GetCostPercent and GetSpeedPercent return the instance count.
        Assert.Equal(1620d, modifier.ToDouble(), 12);
    }

    [Fact]
    public void SupportsNonMonotonicAuthoredScalingWithoutSearchAssumptions()
    {
        var cost = new[]
        {
            new GameValueModifier(GameValueModifierType.Raw, -0.4d),
            new GameValueModifier(GameValueModifierType.MultiStacking, 2d, order: 1),
        };
        var scratch = new GameValueModifier[2];

        Assert.True(TryScale(
            level: 2,
            hasInstanceScaling: true,
            cost,
            System.Array.Empty<GameValueModifier>(),
            scratch,
            out var levelTwo));
        Assert.True(TryScale(
            level: 3,
            hasInstanceScaling: true,
            cost,
            System.Array.Empty<GameValueModifier>(),
            scratch,
            out var levelThree));

        Assert.True(levelThree < levelTwo);
    }

    [Fact]
    public void RejectsInvalidLevelValuesAndInsufficientScratch()
    {
        var modifier = new[]
        {
            new GameValueModifier(GameValueModifierType.Raw, 1d),
        };

        Assert.False(TryScale(
            level: 0,
            hasInstanceScaling: true,
            modifier,
            modifier,
            new GameValueModifier[1],
            out _));
        Assert.False(TryScale(
            level: 1,
            hasInstanceScaling: true,
            modifier,
            modifier,
            System.Array.Empty<GameValueModifier>(),
            out _));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidResolvedRecords(double invalid)
    {
        Assert.False(GameHarvestActionScalingMath.TryGetDrainCostModifier(
            level: 1,
            hasInstanceScaling: true,
            actionCostModifier: new BigDouble(invalid),
            actionSpeed: 100d,
            elementActionCostModifier: 100d,
            elementActionSpeed: 100d,
            costPerInstance: System.Array.Empty<GameValueModifier>(),
            speedPerInstance: System.Array.Empty<GameValueModifier>(),
            scratch: System.Array.Empty<GameValueModifier>(),
            out _));
    }

    private static bool TryScale(
        int level,
        bool hasInstanceScaling,
        GameValueModifier[] cost,
        GameValueModifier[] speed,
        GameValueModifier[] scratch,
        out BigDouble modifier) =>
        GameHarvestActionScalingMath.TryGetDrainCostModifier(
            level,
            hasInstanceScaling,
            actionCostModifier: 120d,
            actionSpeed: 80d,
            elementActionCostModifier: 150d,
            elementActionSpeed: 125d,
            cost,
            speed,
            scratch,
            out modifier);
}
