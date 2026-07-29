using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// Auto Buy's reading of the published effect table: which authored effects make a candidate worth
/// preferring, and which ones only look like they do.
/// </summary>
/// <remarks>
/// Fixture identities are the real UUIDs from <c>data/entity-display-names.tsv</c>, so the kind each
/// target resolves as is the kind the game gives it rather than one the fixture asserts.
/// </remarks>
public sealed class AutoBuyEconomicPriorityPolicyTests
{
    private static readonly Guid Cauldron = new("182ce873-3b20-4e74-8c5f-07f057666871");
    private static readonly Guid Anvil = new("046b7ce4-af43-4020-b7e6-f774d6187204");
    private static readonly Guid AlchemicDial = new("0446814d-5261-482a-913c-70a3174c658a");
    private static readonly Guid Mana = new("b11072bf-7980-4e23-bc6c-8034ba09b925");
    private static readonly Guid ExpertAlchemy = new("064d395a-4667-4bbb-b3b4-91bb03f67ba3");

    [Fact]
    public void ACandidateThatAuthorsNoEffectsIsWorthNoPreference()
    {
        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, World()));
    }

    [Theory]
    [InlineData("Cost")]
    [InlineData("CostScaling")]
    public void LoweringAnotherStructuresCostIsWorthPreferring(string property)
    {
        var world = World(Lowers(Cauldron, Anvil, property));

        Assert.Equal(AutoBuyEconomicPriority.CostReduction, Classify(Cauldron, world));
    }

    /// <summary>
    /// The direction of the modifier is the whole claim: the same property moved the other way is a
    /// candidate that makes everything more expensive.
    /// </summary>
    [Fact]
    public void RaisingAnotherStructuresCostIsNotACostReduction()
    {
        var world = World(Raises(Cauldron, Anvil, "Cost"));

        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, world));
    }

    [Fact]
    public void RaisingAResourcesQualityIsWorthPreferring()
    {
        var world = World(Raises(Cauldron, Mana, "Quality"));

        Assert.Equal(AutoBuyEconomicPriority.QualityIncrease, Classify(Cauldron, world));
    }

    [Fact]
    public void LoweringAResourcesQualityIsNotAQualityIncrease()
    {
        var world = World(Lowers(Cauldron, Mana, "Quality"));

        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, world));
    }

    [Fact]
    public void RaisingAResourcesAttributeCostIsNotACostReduction()
    {
        var world = World(Raises(Cauldron, Mana, "AttributeCost"));

        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, world));
    }

    /// <summary>
    /// Both spellings of the resource attribute-cost property count, because both reach this table.
    /// </summary>
    /// <remarks>
    /// <c>AttributeCostMod</c> is the member name a resource effect carries from the game's
    /// <c>ModifiableType</c> enum; <c>AttributeCost</c> is the string an upgradeable-object effect
    /// carries from a resource's authored property record. Accepting only one would silently drop
    /// every candidate that authors its discount the other way.
    /// </remarks>
    [Theory]
    [InlineData("AttributeCost")]
    [InlineData("AttributeCostMod")]
    public void LoweringAResourcesAttributeCostIsWorthPreferring(string property)
    {
        var world = World(Lowers(Cauldron, Mana, property));

        Assert.Equal(AutoBuyEconomicPriority.CostReduction, Classify(Cauldron, world));
    }

    /// <summary>
    /// A property name means what the kind of thing it names means by it, and the two vocabularies do
    /// not overlap.
    /// </summary>
    /// <remarks>
    /// A structure has no quality and a resource has no purchase cost, so reading either name against
    /// the wrong kind of target would be inventing a claim the build never authored. The target's own
    /// table is what decides which reading applies.
    /// </remarks>
    [Theory]
    [InlineData("Quality", true)]
    [InlineData("Cost", false)]
    public void APropertyNameIsOnlyReadAgainstTheKindOfTargetThatUsesIt(string property, bool structureTarget)
    {
        var effect = structureTarget
            ? Raises(Cauldron, Anvil, property)
            : Lowers(Cauldron, Mana, property);

        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, World(effect)));
    }

    /// <summary>
    /// An effect on something that is neither a resource nor a structure is not a claim Auto Buy can
    /// price, whatever it is named.
    /// </summary>
    [Fact]
    public void AnEffectOnATargetOfSomeOtherKindIsWorthNothing()
    {
        var world = World(Lowers(Cauldron, ExpertAlchemy, "Cost"));

        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, world));
    }

    /// <summary>
    /// An effect whose ratio the suite could not compute is worth nothing rather than assumed
    /// harmless.
    /// </summary>
    /// <remarks>
    /// The unknown ratio is published as one, which compares as "does not lower the cost" and would
    /// reach the same answer here by luck. It is asserted on its own because the reason matters: a
    /// modifier kind this build has that the port does not is a gap to notice, not a neutral effect.
    /// </remarks>
    [Fact]
    public void AnEffectWhoseModifierThePortDoesNotModelIsWorthNothing()
    {
        var world = World(new RawEntityEffect(Cauldron, Anvil, "Cost", modifierType: 99, new BigDouble(1d)));

        Assert.False(world.EntityEffects.AsSpan()[0].RatioKnown);
        Assert.Equal(AutoBuyEconomicPriority.None, Classify(Cauldron, world));
    }

    [Fact]
    public void ACandidateThatBothCheapensAndImprovesCarriesBothPreferences()
    {
        var world = World(
            Lowers(Cauldron, Anvil, "Cost"),
            Raises(Cauldron, Mana, "Quality"));

        Assert.Equal(
            AutoBuyEconomicPriority.CostReduction | AutoBuyEconomicPriority.QualityIncrease,
            Classify(Cauldron, world));
    }

    /// <summary>
    /// A candidate is judged on the effects it authors, not on the ones authored about it.
    /// </summary>
    /// <remarks>
    /// The table is keyed by source and holds both directions of the same pair here, so a lookup that
    /// matched on the target instead would find a row and answer confidently with someone else's
    /// claim.
    /// </remarks>
    [Fact]
    public void EachCandidateIsJudgedOnTheEffectsItAuthors()
    {
        var world = World(
            Lowers(AlchemicDial, Cauldron, "Cost"),
            Raises(Cauldron, Mana, "Quality"));

        Assert.Equal(AutoBuyEconomicPriority.QualityIncrease, Classify(Cauldron, world));
        Assert.Equal(AutoBuyEconomicPriority.CostReduction, Classify(AlchemicDial, world));
    }

    private static AutoBuyEconomicPriority Classify(Guid candidateId, GameWorldState world) =>
        AutoBuyEconomicPriorityPolicy.Classify(world, candidateId);

    private static RawEntityEffect Lowers(Guid source, Guid target, string property) =>
        new(source, target, property, (int)GameValueModifierType.Reduction, BigDouble.One);

    private static RawEntityEffect Raises(Guid source, Guid target, string property) =>
        new(source, target, property, (int)GameValueModifierType.MultiDiminishing, new BigDouble(0.5d));

    /// <summary>
    /// A world holding the three fixture structures and the fixture resource, plus whichever effects
    /// the test authors, derived through the same deriver the worker runs.
    /// </summary>
    private static GameWorldState World(params RawEntityEffect[] effects)
    {
        var buffer = new WorldEntityEffectBuffer();
        for (var index = 0; index < effects.Length; index++) buffer.Append(in effects[index]);

        return new GameWorldState
        {
            Resources = Derive(new WorldResourceDeriver(default), WorldSamples.Resource(Mana, 1d, -1d)),
            Structures = Derive(
                WorldStructureDeriver.Shared,
                WorldSamples.Structure(Cauldron),
                WorldSamples.Structure(Anvil),
                WorldSamples.Structure(AlchemicDial)),
            EntityEffects = new WorldEntityEffectDeriver().Build(buffer),
        };
    }

    private static PublicationTable<TRow> Derive<TSample, TRow>(
        WorldRowDeriver<TSample, TRow> deriver,
        params TSample[] samples)
        where TSample : struct, IWorldEntity
        where TRow : struct, IWorldEntity
    {
        var rows = new TRow[samples.Length];
        for (var index = 0; index < samples.Length; index++) rows[index] = deriver.Derive(in samples[index]);
        return WorldTable.Create(rows);
    }
}
