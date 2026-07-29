using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyCompactParityTests
{
    [Fact]
    public void CompactRawScalingMatchesExpandedNativeOracleFixture()
    {
        var compact = Compact(
            baseline: 10,
            quality: 100,
            baseCost: 4,
            maximumLevel: 3,
            scaling: Neutral(),
            costPerInstance: new[]
            {
                new GameValueModifier(GameValueModifierType.Raw, 1.5d),
            });
        var nativeOracle = Expanded(
            baseline: 10,
            drains: new[] { 4d, 10d, 16d });

        AssertSameDecision(nativeOracle, compact);
        Assert.Equal(2, compact.TargetLevel);
    }

    [Fact]
    public void CompactQualityAndFullSpeedMatchExpandedNativeOracleFixture()
    {
        var compact = Compact(
            baseline: 13,
            quality: 200,
            baseCost: 10,
            maximumLevel: 3,
            scaling: new AutoAgromancyScalingSnapshot(
                hasInstanceScaling: true,
                actionCostModifier: 120d,
                actionSpeed: 80d,
                elementActionCostModifier: 150d,
                elementActionSpeed: 125d),
            costPerInstance: new[]
            {
                new GameValueModifier(GameValueModifierType.Raw, 0.25d),
            },
            speedPerInstance: new[]
            {
                new GameValueModifier(
                    GameValueModifierType.MultiDiminishing,
                    0.1d),
            });
        var nativeOracle = Expanded(
            baseline: 13,
            drains: new[] { 9d, 12.375d, 16.2d });

        AssertSameDecision(nativeOracle, compact);
        Assert.Equal(2, compact.TargetLevel);
    }

    [Fact]
    public void CompactNonMonotonicScalingMatchesExpandedNativeOracleFixture()
    {
        var compact = Compact(
            baseline: 5,
            quality: 100,
            baseCost: 5,
            maximumLevel: 3,
            scaling: Neutral(),
            costPerInstance: new[]
            {
                new GameValueModifier(GameValueModifierType.Raw, -0.4d),
                new GameValueModifier(
                    GameValueModifierType.MultiStacking,
                    2d,
                    order: 1),
            });
        var nativeOracle = Expanded(
            baseline: 5,
            drains: new[] { 5d, 6d, 4d });

        AssertSameDecision(nativeOracle, compact);
        Assert.Equal(3, compact.TargetLevel);
    }

    private static AutoAgromancyCompactPlan Compact(
        double baseline,
        double quality,
        double baseCost,
        int maximumLevel,
        AutoAgromancyScalingSnapshot scaling,
        GameValueModifier[]? costPerInstance = null,
        GameValueModifier[]? speedPerInstance = null)
    {
        var resources = new[]
        {
            new AutoAgromancyCompactResource(
                Guid.Parse("d760122e-c77c-43e5-b31c-78381ed8a80d"),
                "resource",
                baseline,
                quality),
        };
        var costs = new[] { new AutoAgromancyBaseCost(0, baseCost) };
        return AutoAgromancyCompactLevelPlanner.Plan(
            maximumLevel,
            resources,
            costs,
            in scaling,
            costPerInstance ?? Array.Empty<GameValueModifier>(),
            speedPerInstance ?? Array.Empty<GameValueModifier>());
    }

    private static AutoAgromancyPlan Expanded(
        double baseline,
        double[] drains)
    {
        var levels = new AutoAgromancyLevelCost[drains.Length];
        for (var index = 0; index < drains.Length; index++)
        {
            levels[index] = new AutoAgromancyLevelCost(
                index + 1,
                new[] { new AutoAgromancyDrainEntry(0, Amount(drains[index])) });
        }
        return AutoAgromancyLevelPlanner.Plan(
            drains.Length,
            new[]
            {
                new AutoAgromancyResourceSnapshot(
                    "d760122e-c77c-43e5-b31c-78381ed8a80d",
                    "resource",
                    Amount(baseline)),
            },
            levels);
    }

    private static AutoAgromancyScalingSnapshot Neutral() =>
        new(
            hasInstanceScaling: true,
            actionCostModifier: 100d,
            actionSpeed: 100d,
            elementActionCostModifier: 100d,
            elementActionSpeed: 100d);

    private static BigAmount Amount(double value) =>
        value == 0d ? default : new BigAmount(value, 0);

    private static void AssertSameDecision(
        AutoAgromancyPlan expanded,
        AutoAgromancyCompactPlan compact)
    {
        Assert.Equal(expanded.Disposition, compact.Disposition);
        Assert.Equal(expanded.TargetLevel, compact.TargetLevel);
        Assert.Equal(expanded.LimitingResourceIndex, compact.LimitingResourceIndex);
    }
}
