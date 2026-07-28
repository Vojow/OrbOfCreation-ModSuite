using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The identity walk feeds the live collision check, and a walk that quietly skips a table would make
/// that check weaker rather than fail it. These tests are mostly about the skipping.
/// </summary>
public sealed class WorldIdentityWalkTests
{
    [Fact]
    public void AnEmptySnapshotYieldsNothing()
    {
        Assert.Empty(WorldIdentityWalk.Enumerate(GameWorldStateDefaults.Empty));
    }

    [Fact]
    public void EveryRowInEveryTableIsVisited()
    {
        var mana = Guid.NewGuid();
        var stone = Guid.NewGuid();
        var hoard = Guid.NewGuid();

        var world = new GameWorldState
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(mana, new BigDouble(1d), isPercent: false),
                new WorldNumberVariable(stone, new BigDouble(2d), isPercent: false)),
            TreasurePools = WorldTable.Create(
                new WorldTreasurePool(hoard, 3, new BigDouble(0.5d), false, 1, false)),
        };

        Assert.Equal(
            new[] { mana, stone, hoard }.OrderBy(id => id),
            WorldIdentityWalk.Enumerate(world).OrderBy(id => id));
    }

    /// <summary>
    /// The collision check exists to find exactly this, and it can only find it if the walk reports
    /// the same identity twice rather than deduplicating on the way out.
    /// </summary>
    [Fact]
    public void AnIdentityHeldByTwoTablesIsYieldedTwice()
    {
        var shared = Guid.NewGuid();

        var world = new GameWorldState
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(shared, new BigDouble(1d), isPercent: false)),
            DoubleVariables = WorldTable.Create(
                new WorldNumberVariable(shared, new BigDouble(2d), isPercent: false)),
        };

        Assert.Equal(2, WorldIdentityWalk.Enumerate(world).Count(id => id == shared));
    }

    /// <summary>
    /// Tables the walk deliberately does not read, and why.
    /// </summary>
    /// <remarks>
    /// <c>PurchaseCosts</c> is several rows per entity, keyed by an identity the structures table
    /// already owns. Walking it would report every priced structure as colliding with itself, which
    /// would turn the collision check from a real invariant into noise everyone learns to ignore.
    /// <c>PlotActions</c> is worse: its rows are pairs, so neither of the two identities on one is
    /// the row's own, and both belong to a table that already claims them. <c>EntityEffects</c> is
    /// the same shape — an edge from one claimed entity to another, and <c>PlotActionInstances</c>
    /// is that edge several times over, one row per instance the plot holds. <c>PlotAuthoring</c>,
    /// <c>PlotPhaseDescriptors</c> and <c>EffectBlocks</c> are all second readings of an entity the
    /// plot and action tables already claim, said about the entity rather than as it. So is
    /// <c>EntityRequirements</c>, whose rows are conditions on an upgrade or a structure the two
    /// purchasable categories already own. <c>ActionQueueSlots</c> is a position in a list, which is
    /// no entity at all.
    /// <para>
    /// <c>ActionQueues</c> is not among them: a queue is a list variable with a uuid of its own that
    /// no other category collects, so it is walked like any other entity.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> NotIdentityTables = new(StringComparer.Ordinal)
    {
        "PurchaseCosts",
        "PlotActions",
        "PlotActionInstances",
        "EntityEffects",
        "ActionQueueSlots",
        "SpellSlots",
        "SpellCosts",
        "ConceptRecipes",
        "AlchemyInstances",
        "AlchemyCosts",
        "PlotAuthoring",
        "PlotPhaseDescriptors",
        "EffectBlocks",
        "EntityRequirements",
    };

    /// <summary>
    /// The walk selects tables by "row implements <c>IWorldEntity</c>", so a table added later whose
    /// row does not would be skipped in silence. That is the one way this can rot, so it is asserted
    /// against the snapshot's real shape rather than against a fixture — with every exclusion named
    /// above rather than merely happening.
    /// </summary>
    [Fact]
    public void EveryPublishedTableHoldsRowsTheWalkCanRead()
    {
        var unreadable = new List<string>();

        var properties = typeof(GameWorldState).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var tables = 0;
        foreach (var property in properties)
        {
            var type = property.PropertyType;
            if (!type.IsGenericType) continue;
            if (type.GetGenericTypeDefinition() != typeof(PublicationTable<>)) continue;

            tables++;
            if (NotIdentityTables.Contains(property.Name)) continue;

            var row = type.GetGenericArguments()[0];
            if (!typeof(IWorldEntity).IsAssignableFrom(row)) unreadable.Add(property.Name);
        }

        Assert.True(tables > 0, "the snapshot published no tables at all; the filter must be wrong");
        Assert.True(
            unreadable.Count == 0,
            $"these tables hold rows without an identity the walk can read: {string.Join(", ", unreadable)}");
    }
}
