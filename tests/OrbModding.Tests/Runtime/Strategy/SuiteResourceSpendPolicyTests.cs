using System;
using OrbModding.Common.Runtime.Strategy;
using Xunit;

namespace OrbModding.Tests.Runtime.Strategy;

/// <summary>
/// The spend predicate is where strategy becomes behavior, so these tests are written as the
/// gameplay situations that motivated it rather than as branch coverage. The precedence tests are
/// the load-bearing ones: strategy must never be able to permit what configuration refused.
/// </summary>
public sealed class SuiteResourceSpendPolicyTests
{
    private static readonly Guid Knowledge = new("22222222-2222-2222-2222-222222222222");
    private static readonly BigDouble NoReserve = default;

    private static SuiteSpendDecision Evaluate(
        in SuiteResourceStance stance,
        double cost,
        double quantity,
        bool hasCapacity = false,
        double capacity = 0d,
        double configuredAbsoluteReserve = 0d,
        double configuredRelativeMultiplier = 0d) =>
        SuiteResourceSpendPolicy.Evaluate(
            in stance,
            new BigDouble(cost),
            new BigDouble(quantity),
            hasCapacity,
            new BigDouble(capacity),
            new BigDouble(configuredAbsoluteReserve),
            configuredRelativeMultiplier);

    [Fact]
    public void ANeutralStanceReproducesConfigurationOnlyBehaviour()
    {
        var free = SuiteResourceStance.Free(Knowledge);

        Assert.True(Evaluate(free, cost: 5, quantity: 10).Allowed);
        Assert.Equal(
            SuiteSpendOutcome.BlockedInsufficientQuantity,
            Evaluate(free, cost: 20, quantity: 10).Outcome);
        Assert.Equal(
            SuiteSpendOutcome.BlockedConfiguredReserve,
            Evaluate(free, cost: 8, quantity: 10, configuredAbsoluteReserve: 5).Outcome);
    }

    [Fact]
    public void TrivialPurchasesAreAllowedWhileSavingButMeaningfulOnesAreNot()
    {
        // The motivating case: saving toward 5 knowledge while sitting on 3. A 0.1-cost upgrade is
        // noise and should still run; a 2-cost upgrade would meaningfully set the goal back.
        var savingUp = SuiteResourceStance.TrivialOnly(Knowledge, maxSpendFraction: 0.05d);

        Assert.Equal(SuiteSpendOutcome.Allowed, Evaluate(savingUp, cost: 0.1d, quantity: 3d).Outcome);
        Assert.Equal(SuiteSpendOutcome.BlockedStrategyRatio, Evaluate(savingUp, cost: 2d, quantity: 3d).Outcome);

        // Configuration alone would have permitted both, so the refusal is strategy's.
        Assert.True(Evaluate(SuiteResourceStance.Free(Knowledge), cost: 2d, quantity: 3d).Allowed);
    }

    [Fact]
    public void AnAbsoluteFloorProtectsTheAmountBeingSavedFor()
    {
        // Saving 200 mana for the regeneration upgrade: spending must leave the 200 intact.
        var floor = SuiteResourceStance.FloorOf(Knowledge, new BigDouble(200));

        Assert.Equal(SuiteSpendOutcome.Allowed, Evaluate(floor, cost: 50, quantity: 300).Outcome);
        Assert.Equal(SuiteSpendOutcome.Allowed, Evaluate(floor, cost: 100, quantity: 300).Outcome);
        Assert.Equal(SuiteSpendOutcome.BlockedStrategyFloor, Evaluate(floor, cost: 101, quantity: 300).Outcome);
    }

    [Fact]
    public void SpendingIsPermittedAgainstAStorageCapWhenTheGoalStillFits()
    {
        // Saving 60 of a 100 cap: at 80 held, a 20 purchase still leaves 60, so it is fine.
        var floor = SuiteResourceStance.FractionOfCapacity(Knowledge, fraction: 0.6d);

        Assert.Equal(
            SuiteSpendOutcome.Allowed,
            Evaluate(floor, cost: 20, quantity: 80, hasCapacity: true, capacity: 100).Outcome);
        Assert.Equal(
            SuiteSpendOutcome.BlockedStrategyFloor,
            Evaluate(floor, cost: 25, quantity: 80, hasCapacity: true, capacity: 100).Outcome);
    }

    [Fact]
    public void AFractionOfCapacityStanceOnAnUncappedResourceIsReportedRatherThanGuessed()
    {
        var floor = SuiteResourceStance.FractionOfCapacity(Knowledge, fraction: 0.6d);
        var decision = Evaluate(floor, cost: 20, quantity: 80, hasCapacity: false);

        Assert.True(decision.Allowed);
        Assert.Equal(SuiteSpendOutcome.AllowedStanceNotApplicable, decision.Outcome);
        Assert.False(decision.BlockedByStrategy);
    }

    [Fact]
    public void AnEmbargoRefusesAnySpendButNotAFreeCostRow()
    {
        var embargo = SuiteResourceStance.Embargo(Knowledge);

        Assert.Equal(SuiteSpendOutcome.BlockedStrategyEmbargo, Evaluate(embargo, cost: 1, quantity: 1e9).Outcome);

        // A candidate that costs nothing of this resource is not constrained by its embargo.
        Assert.Equal(SuiteSpendOutcome.Allowed, Evaluate(embargo, cost: 0, quantity: 1e9).Outcome);
    }

    [Fact]
    public void StrategyCanNeverPermitWhatConfigurationRefused()
    {
        // Every stance, including the most permissive, against a configured reserve that refuses.
        var stances = new[]
        {
            SuiteResourceStance.Free(Knowledge),
            SuiteResourceStance.FloorOf(Knowledge, NoReserve),
            SuiteResourceStance.FractionOfCapacity(Knowledge, 0d),
            SuiteResourceStance.TrivialOnly(Knowledge, 1d),
            SuiteResourceStance.Embargo(Knowledge),
        };

        foreach (var stance in stances)
        {
            var decision = Evaluate(
                stance, cost: 8, quantity: 10, configuredAbsoluteReserve: 5);
            Assert.False(decision.Allowed);
            Assert.Equal(SuiteSpendOutcome.BlockedConfiguredReserve, decision.Outcome);
            Assert.False(decision.BlockedByStrategy);
        }
    }

    [Fact]
    public void TheConfiguredRelativeReserveStillApplies()
    {
        var free = SuiteResourceStance.Free(Knowledge);

        // A 2x relative multiplier requires cost + 2*cost to remain available.
        Assert.Equal(
            SuiteSpendOutcome.Allowed,
            Evaluate(free, cost: 10, quantity: 30, configuredRelativeMultiplier: 2d).Outcome);
        Assert.Equal(
            SuiteSpendOutcome.BlockedConfiguredReserve,
            Evaluate(free, cost: 10, quantity: 29, configuredRelativeMultiplier: 2d).Outcome);

        // A negative multiplier is treated as no relative reserve rather than a negative floor.
        Assert.True(Evaluate(free, cost: 10, quantity: 10, configuredRelativeMultiplier: -5d).Allowed);
    }

    [Fact]
    public void NegativeEvidenceFailsClosed()
    {
        var free = SuiteResourceStance.Free(Knowledge);

        Assert.Equal(SuiteSpendOutcome.BlockedInvalidSnapshot, Evaluate(free, cost: -1, quantity: 10).Outcome);
        Assert.Equal(SuiteSpendOutcome.BlockedInvalidSnapshot, Evaluate(free, cost: 1, quantity: -10).Outcome);
    }

    [Fact]
    public void OutOfRangeStanceFractionsAreClampedRatherThanTrusted()
    {
        // A fraction above one would otherwise demand more than the resource can hold, and a
        // negative one would silently loosen the stance into a negative floor.
        var overOne = SuiteResourceStance.FractionOfCapacity(Knowledge, fraction: 4d);
        Assert.Equal(
            SuiteSpendOutcome.BlockedStrategyFloor,
            Evaluate(overOne, cost: 1, quantity: 80, hasCapacity: true, capacity: 100).Outcome);

        var negative = SuiteResourceStance.FractionOfCapacity(Knowledge, fraction: -1d);
        Assert.Equal(
            SuiteSpendOutcome.Allowed,
            Evaluate(negative, cost: 20, quantity: 80, hasCapacity: true, capacity: 100).Outcome);

        var negativeShare = SuiteResourceStance.TrivialOnly(Knowledge, maxSpendFraction: -1d);
        Assert.Equal(
            SuiteSpendOutcome.BlockedStrategyRatio,
            Evaluate(negativeShare, cost: 1, quantity: 100).Outcome);
    }
}
