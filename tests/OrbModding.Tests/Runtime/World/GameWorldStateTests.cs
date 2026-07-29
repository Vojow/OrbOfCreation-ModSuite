using System;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The world snapshot is a service-cycle publication built from raw main-thread readings, so these
/// tests pin three things the design depends on: it really is publishable, the identity invariants
/// that make the sorted lookups trustworthy hold, and derivation stays faithful to the captured
/// values instead of inventing bounds.
/// </summary>
/// <remarks>
/// Fixture identities are the real UUIDs from <c>data/entity-display-names.tsv</c> rather than
/// invented ones, so a test that resolves Mana is resolving the same entity the game does, and a
/// future mapping refresh that moves an identity shows up here.
/// </remarks>
public sealed class GameWorldStateTests
{
    private static readonly Guid Mana = new("b11072bf-7980-4e23-bc6c-8034ba09b925");
    private static readonly Guid Water = new("eab888ff-d8bd-4e46-81eb-639d5d562242");
    private static readonly Guid Knowledge = new("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
    private static readonly Guid Space = new("9550808a-433c-4320-a4a4-e66e2858a362");
    private static readonly Guid Cauldron = new("182ce873-3b20-4e74-8c5f-07f057666871");
    private static readonly Guid ImprovedAlchemy = new("d4a9711d-e1f8-4951-999c-11e1026e586b");
    private static readonly Guid ExpertAlchemy = new("064d395a-4667-4bbb-b3b4-91bb03f67ba3");

    /// <summary>
    /// Derives samples into a published table exactly as the worker half of collection does, through
    /// the same binder the collector uses. Generic over the category, because everything past the
    /// binder is.
    /// </summary>
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

    [Fact]
    public void TheSnapshotIsAcceptedAsAPublication()
    {
        // Construction runs the structural validator. If a world row ever grows an unpublishable
        // member — an array, a collection, a Unity reference — this fails here rather than in game.
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);

        Assert.Same(GameWorldStateDefaults.Empty, publisher.ReadLatest().Snapshot);
    }

    [Fact]
    public void TheEmptySnapshotAnswersEveryLookupWithoutKnowingAnything()
    {
        // Services start before the first collection completes. "Nothing known yet" must travel the
        // normal lookup path, not a null check.
        var world = GameWorldStateDefaults.Empty;

        Assert.False(WorldLookup.TryFind(world.Resources, Mana, out _));
        Assert.False(WorldLookup.TryFind(world.Structures, Cauldron, out _));
        Assert.False(WorldLookup.TryFind(world.Upgrades, ImprovedAlchemy, out _));
        Assert.False(WorldLookup.TryFind(world.Research, ExpertAlchemy, out _));
        Assert.Equal(0, world.Resources.Count);
    }

    [Fact]
    public void EveryCategoryResolvesByIdentityRegardlessOfSampleOrder()
    {
        var world = new GameWorldState
        {
            Resources = Derive(
                new WorldResourceDeriver(default),
                WorldSamples.Resource(Water, 5d, -1d, 0d, true),
                WorldSamples.Resource(Mana, 100d, 200d, 1d, true),
                WorldSamples.Resource(Knowledge, 3d, -1d, 0d, true)),
            Structures = Derive(WorldStructureDeriver.Shared, WorldSamples.Structure(Cauldron, 4d, 1d, true)),
            Upgrades = Derive(WorldUpgradeDeriver.Shared, WorldSamples.Upgrade(ImprovedAlchemy, 0, 1, true)),
            Research = Derive(WorldIdentityDeriver<WorldResearch>.Shared, WorldSamples.Research(ExpertAlchemy, level: 2, isDeveloping: true)),
        };

        Assert.True(WorldLookup.TryFind(world.Resources, Mana, out var mana));
        Assert.Equal(100d, mana.Reading.Quantity.ToDouble());
        Assert.True(WorldLookup.TryFind(world.Resources, Knowledge, out var knowledge));
        Assert.Equal(3d, knowledge.Reading.Quantity.ToDouble());
        Assert.True(WorldLookup.TryFind(world.Structures, Cauldron, out var cauldron));
        Assert.Equal(5d, cauldron.CommittedLevel.ToDouble());
        Assert.True(WorldLookup.TryFind(world.Upgrades, ImprovedAlchemy, out var upgrade));
        Assert.Equal(1, upgrade.RemainingLevels);
        Assert.True(WorldLookup.TryFind(world.Research, ExpertAlchemy, out var research));
        Assert.True(research.IsDeveloping);

        // An identity never sampled is absent, not a nearby row the binary search settled on.
        Assert.False(WorldLookup.TryFind(world.Resources, Space, out _));
    }

    [Fact]
    public void TablesAreSortedSoTheBinarySearchIsValid()
    {
        var resources = Derive(
            new WorldResourceDeriver(default),
            WorldSamples.Resource(Water, 1d, -1d, 0d, true),
            WorldSamples.Resource(Mana, 1d, -1d, 0d, true),
            WorldSamples.Resource(Space, 1d, -1d, 0d, true),
            WorldSamples.Resource(Knowledge, 1d, -1d, 0d, true));

        var rows = resources.AsSpan();
        for (var index = 1; index < rows.Length; index++)
        {
            Assert.True(
                rows[index - 1].EntityId.CompareTo(rows[index].EntityId) < 0,
                "world rows must be strictly ascending by identity");
        }

        // Every sampled identity is still reachable after the sort.
        foreach (var id in new[] { Water, Mana, Space, Knowledge })
        {
            Assert.True(WorldLookup.TryFind(resources, id, out _));
        }
    }

    [Fact]
    public void UnidentifiedAndRepeatedRowsAreRefusedWhenTheTableIsBuilt()
    {
        // A duplicate would make a sorted lookup return an arbitrary member of the pair.
        Assert.Throws<ArgumentException>(() => Derive(
            new WorldResourceDeriver(default),
            WorldSamples.Resource(Mana, 1d, -1d, 0d, true),
            WorldSamples.Resource(Mana, 2d, -1d, 0d, true)));

        // An unidentified row is indistinguishable from an uninitialized one.
        Assert.Throws<ArgumentException>(() => Derive(
            new WorldResourceDeriver(default), WorldSamples.Resource(Guid.Empty, 1d, -1d, 0d, true)));

        // Categories claim identities independently, so the same UUID in two tables is not a clash
        // here. Rejecting that is the collector's job, because only the collector sees both at once.
        Derive(new WorldResourceDeriver(default), WorldSamples.Resource(Mana, 1d, -1d, 0d, true));
        Derive(WorldStructureDeriver.Shared, WorldSamples.Structure(Mana, 1d, 0d, true));
    }

    [Fact]
    public void APublishedTableKeepsNoPathBackToTheRowsItWasBuiltFrom()
    {
        // The whole bargain of an immutable publication: a consumer that has pinned a snapshot must
        // not see it change because the producer reused its scratch buffer for the next cycle — which
        // is exactly what the category readers do.
        var deriver = new WorldResourceDeriver(default);
        var kept = WorldSamples.Resource(Mana, 10d);
        var rows = new[] { deriver.Derive(in kept) };
        var table = WorldTable.Create(rows);

        var overwritten = WorldSamples.Resource(Water, 999d);
        rows[0] = deriver.Derive(in overwritten);

        Assert.Equal(1, table.Count);
        Assert.True(WorldLookup.TryFind(table, Mana, out var pinned));
        Assert.Equal(10d, pinned.Reading.Quantity.ToDouble());
        Assert.False(WorldLookup.TryFind(table, Water, out _));
    }
}
