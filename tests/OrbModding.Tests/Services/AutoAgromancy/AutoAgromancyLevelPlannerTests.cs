using System.Collections.Generic;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoAgromancy;

public sealed class AutoAgromancyLevelPlannerTests
{
    [Fact]
    public void SelectsHighestLevelAndAllowsExactZeroProjectedRate()
    {
        var result = Plan(
            resources: new[] { Resource("mana", 10) },
            Level(1, Drain(0, 4)),
            Level(2, Drain(0, 10)),
            Level(3, Drain(0, 11)));

        Assert.Equal(AutoAgromancyPlanDisposition.Selected, result.Disposition);
        Assert.Equal(2, result.TargetLevel);
        Assert.True(result.LimitingProjectedRate.IsZero);
    }

    [Fact]
    public void ExactSearchDoesNotAssumeMonotonicCosts()
    {
        var result = Plan(
            resources: new[] { Resource("mana", 10) },
            Level(1, Drain(0, 5)),
            Level(2, Drain(0, 12)),
            Level(3, Drain(0, 8)));

        Assert.Equal(3, result.TargetLevel);
    }

    [Fact]
    public void ExistingSelectedContributionCanBeAddedBackToBaseline()
    {
        // Native live rate 6 plus the selected action's current drain 4.
        var result = Plan(
            resources: new[] { Resource("mana", 10) },
            Level(1, Drain(0, 8)));

        Assert.Equal(1, result.TargetLevel);
        Assert.Equal("2e0", result.LimitingProjectedRate.ToString());
    }

    [Fact]
    public void MultipleResourcesUseTheTightestProjectedRate()
    {
        var result = Plan(
            resources: new[]
            {
                Resource("mana", 20),
                Resource("water", 7),
            },
            Level(1, Drain(0, 10), Drain(1, 6)),
            Level(2, Drain(0, 15), Drain(1, 8)));

        Assert.Equal(1, result.TargetLevel);
        Assert.Equal(1, result.LimitingResourceIndex);
        Assert.Equal("1e0", result.LimitingProjectedRate.ToString());
    }

    [Fact]
    public void RejectsWhenLevelOneWouldDrainAResource()
    {
        var result = Plan(
            resources: new[] { Resource("mana", 0) },
            Level(1, Drain(0, 1)));

        Assert.Equal(
            AutoAgromancyPlanDisposition.LevelOneUnsustainable,
            result.Disposition);
        Assert.False(result.HasTarget);
        Assert.Equal(0, result.LimitingResourceIndex);
        Assert.True(result.LimitingProjectedRate.IsNegative);
    }

    [Fact]
    public void EmptyCostVectorSelectsTheNativeMaximum()
    {
        var result = Plan(
            resources: System.Array.Empty<AutoAgromancyResourceSnapshot>(),
            Level(1),
            Level(2),
            Level(3));

        Assert.Equal(3, result.TargetLevel);
        Assert.Equal(-1, result.LimitingResourceIndex);
    }

    [Fact]
    public void RejectsIncompleteAndNegativeSnapshots()
    {
        var incomplete = AutoAgromancyLevelPlanner.Plan(
            2,
            new[] { Resource("mana", 10) },
            new[] { Level(1, Drain(0, 1)) });
        var negative = Plan(
            resources: new[] { Resource("mana", 10) },
            Level(1, Drain(0, -1)));

        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, incomplete.Disposition);
        Assert.Equal(AutoAgromancyPlanDisposition.InvalidSnapshot, negative.Disposition);
    }

    [Fact]
    public void RejectsUnauditedMaximumBeforeAllocatingLevelWork()
    {
        var result = AutoAgromancyLevelPlanner.Plan(
            AutoAgromancyLevelPlanner.MaximumExactLevels + 1,
            System.Array.Empty<AutoAgromancyResourceSnapshot>(),
            System.Array.Empty<AutoAgromancyLevelCost>());

        Assert.Equal(AutoAgromancyPlanDisposition.WorkLimitExceeded, result.Disposition);
    }

    private static AutoAgromancyPlan Plan(
        IReadOnlyList<AutoAgromancyResourceSnapshot> resources,
        params AutoAgromancyLevelCost[] levels) =>
        AutoAgromancyLevelPlanner.Plan(levels.Length, resources, levels);

    private static AutoAgromancyResourceSnapshot Resource(string id, double baseline) =>
        new(id, id, Amount(baseline));

    private static AutoAgromancyLevelCost Level(
        int level,
        params AutoAgromancyDrainEntry[] drains) =>
        new(level, drains);

    private static AutoAgromancyDrainEntry Drain(int resource, double amount) =>
        new(resource, Amount(amount));

    private static BigAmount Amount(double value) =>
        value == 0
            ? default
            : new BigAmount(value, 0);
}
