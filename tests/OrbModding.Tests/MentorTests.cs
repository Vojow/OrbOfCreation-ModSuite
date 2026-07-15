using System.Linq;
using OrbMentor;
using Xunit;

namespace OrbModding.Tests;

public sealed class MentorTests
{
    private static readonly MentorRecipe Mentor = new("mentor", 5, true);
    private static readonly MentorRecipe[] Recipes =
    {
        Mentor, new("z-recipient", 4, true), new("a-recipient", 0, true),
        new("equal", 5, true), new("locked", 0, false), new("higher", 6, false),
    };

    [Fact]
    public void HighestDiscoveredSourceMentorsEveryLowerDiscoveredRecipeInStableOrder()
    {
        var recipients = new MentorEngine().EligibleRecipients("mentor", Recipes);
        Assert.Equal(new[] { "a-recipient", "z-recipient" }, recipients.Select(r => r.Uuid));
    }

    [Fact]
    public void TiedMentorQualifiesButLowerSourceDoesNot()
    {
        var engine = new MentorEngine();
        Assert.Equal(2, engine.EligibleRecipients("equal", Recipes).Count);
        Assert.Empty(engine.EligibleRecipients("a-recipient", Recipes));
    }

    [Theory]
    [InlineData(MentorEconomyMode.SharedPool, 5.0, 1998L)]
    [InlineData(MentorEconomyMode.PerRecipient, 1.0, 1999L)]
    public void EconomyFormulaIsExactForRecipients(MentorEconomyMode mode, double expectedMantissa, long expectedExponent)
    {
        var engine = new MentorEngine();
        var recipients = engine.EligibleRecipients("mentor", Recipes);
        var grants = engine.Plan(new MentorAmount(1.0, 2000), 10.0, mode, recipients);
        Assert.All(grants, grant => { Assert.Equal(expectedMantissa, grant.Amount.Mantissa, 12); Assert.Equal(expectedExponent, grant.Amount.Exponent); });
    }

    [Fact]
    public void InvalidAndZeroConfigurationCreatesNoWork()
    {
        var engine = new MentorEngine();
        Assert.Empty(engine.Plan(default, 10, MentorEconomyMode.SharedPool, new[] { Mentor }));
        Assert.Empty(engine.Plan(new MentorAmount(1, 0), double.NaN, MentorEconomyMode.SharedPool, new[] { Mentor }));
        Assert.Empty(engine.Plan(new MentorAmount(1, 0), 0, MentorEconomyMode.SharedPool, new[] { Mentor }));
    }

    [Fact]
    public void PendingWorkConsolidatesSpansBudgetsAndCancels()
    {
        var engine = new MentorEngine();
        engine.Consolidate(new[] { new MentorGrant("b", new MentorAmount(1, 3)), new MentorGrant("b", new MentorAmount(2, 3)), new MentorGrant("a", new MentorAmount(1, 2)) });
        var first = Assert.Single(engine.Take(1));
        Assert.Equal("a", first.Uuid);
        Assert.Equal(1, engine.PendingCount);
        Assert.Equal(3, Assert.Single(engine.Take(1)).Amount.Mantissa, 12);
        engine.Consolidate(new[] { new MentorGrant("x", new MentorAmount(1, 1)) });
        engine.Cancel();
        Assert.Equal(0, engine.PendingCount);
    }
}
