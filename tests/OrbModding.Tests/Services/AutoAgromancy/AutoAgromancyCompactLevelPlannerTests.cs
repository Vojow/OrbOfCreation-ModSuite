using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyCompactLevelPlannerTests
{
    private static readonly AutoAgromancyScalingSnapshot NeutralScaling =
        new(
            hasInstanceScaling: true,
            actionCostModifier: 100d,
            actionSpeed: 100d,
            elementActionCostModifier: 100d,
            elementActionSpeed: 100d);

    [Fact]
    public void SelectsHighestSustainableLevelFromCompactInputs()
    {
        var result = Plan(
            maximumLevel: 3,
            resources: new[] { Resource("mana", baseline: 10, quality: 100) },
            costs: new[] { Cost(0, 4) },
            costScaling: new[]
            {
                new GameValueModifier(GameValueModifierType.Raw, 1.5d),
            });

        Assert.Equal(AutoAgromancyPlanDisposition.Selected, result.Disposition);
        Assert.Equal(2, result.TargetLevel);
        Assert.Equal(0d, result.LimitingProjectedRate.ToDouble(), 12);
    }

    [Fact]
    public void ExactSearchDoesNotAssumeMonotonicScaling()
    {
        var result = Plan(
            maximumLevel: 3,
            resources: new[] { Resource("mana", baseline: 10, quality: 100) },
            costs: new[] { Cost(0, 5) },
            costScaling: new[]
            {
                new GameValueModifier(GameValueModifierType.Raw, -0.4d),
                new GameValueModifier(
                    GameValueModifierType.MultiStacking,
                    2d,
                    order: 1),
            });

        Assert.Equal(3, result.TargetLevel);
    }

    [Fact]
    public void AppliesQualityAndPreservesDuplicateCostTupleOrder()
    {
        var result = Plan(
            maximumLevel: 1,
            resources: new[] { Resource("sap", baseline: 10, quality: 200) },
            costs: new[]
            {
                Cost(0, 6), // element-internal cost, prepended by native code
                Cost(0, 14),
            });

        Assert.Equal(1, result.TargetLevel);
        Assert.Equal(0d, result.LimitingProjectedRate.ToDouble(), 12);
    }

    [Fact]
    public void ExactZeroCostAndEmptyResourceSetSelectMaximum()
    {
        var zero = Plan(
            maximumLevel: 2,
            resources: new[] { Resource("mana", baseline: 0, quality: 100) },
            costs: new[] { Cost(0, 0) });
        var empty = Plan(
            maximumLevel: 4096,
            resources: Array.Empty<AutoAgromancyCompactResource>(),
            costs: Array.Empty<AutoAgromancyBaseCost>());

        Assert.Equal(2, zero.TargetLevel);
        Assert.Equal(4096, empty.TargetLevel);
    }

    [Fact]
    public void LevelOneUnsustainableReportsLimitingResource()
    {
        var result = Plan(
            maximumLevel: 1,
            resources: new[]
            {
                Resource("mana", baseline: 20, quality: 100),
                Resource("water", baseline: 7, quality: 100),
            },
            costs: new[]
            {
                Cost(0, 10),
                Cost(1, 8),
            });

        Assert.Equal(
            AutoAgromancyPlanDisposition.LevelOneUnsustainable,
            result.Disposition);
        Assert.Equal(1, result.LimitingResourceIndex);
        Assert.Equal(-1d, result.LimitingProjectedRate.ToDouble(), 12);
    }

    [Fact]
    public void RejectsInvalidIdentityQualityCostAndModifier()
    {
        var duplicateId = Id("mana");
        var duplicate = Plan(
            maximumLevel: 1,
            resources: new[]
            {
                new AutoAgromancyCompactResource(duplicateId, "a", 1d, 100d),
                new AutoAgromancyCompactResource(duplicateId, "b", 1d, 100d),
            },
            costs: Array.Empty<AutoAgromancyBaseCost>());
        var quality = Plan(
            maximumLevel: 1,
            resources: new[] { Resource("mana", 1, 0) },
            costs: Array.Empty<AutoAgromancyBaseCost>());
        var cost = Plan(
            maximumLevel: 1,
            resources: new[] { Resource("mana", 1, 100) },
            costs: new[] { Cost(0, -1) });
        var modifier = Plan(
            maximumLevel: 1,
            resources: new[] { Resource("mana", 1, 100) },
            costs: Array.Empty<AutoAgromancyBaseCost>(),
            costScaling: new[]
            {
                new GameValueModifier(
                    GameValueModifierType.Raw,
                    BigDouble.NaN),
            });

        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, duplicate.Disposition);
        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, quality.Disposition);
        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, cost.Disposition);
        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, modifier.Disposition);
    }

    [Fact]
    public void RejectsWorkAboveExactLimitBeforeLevelScan()
    {
        var result = Plan(
            AutoAgromancyLevelPlanner.MaximumExactLevels + 1,
            Array.Empty<AutoAgromancyCompactResource>(),
            Array.Empty<AutoAgromancyBaseCost>());

        Assert.Equal(AutoAgromancyPlanDisposition.WorkLimitExceeded, result.Disposition);
    }

    private static AutoAgromancyCompactPlan Plan(
        int maximumLevel,
        AutoAgromancyCompactResource[] resources,
        AutoAgromancyBaseCost[] costs,
        GameValueModifier[]? costScaling = null,
        GameValueModifier[]? speedScaling = null) =>
        AutoAgromancyCompactLevelPlanner.Plan(
            maximumLevel,
            resources,
            costs,
            in NeutralScaling,
            costScaling ?? Array.Empty<GameValueModifier>(),
            speedScaling ?? Array.Empty<GameValueModifier>());

    private static AutoAgromancyCompactResource Resource(
        string id,
        double baseline,
        double quality) =>
        new(Id(id), id, baseline, quality);

    private static AutoAgromancyBaseCost Cost(int resource, double amount) =>
        new(resource, amount);

    private static Guid Id(string value) =>
        new(
            BitConverter.ToInt32(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value)),
                0),
            0,
            0,
            new byte[8]);
}
