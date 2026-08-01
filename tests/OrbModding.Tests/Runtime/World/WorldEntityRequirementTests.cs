using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The per-level prerequisite container becomes published rows and one same-generation native verdict.
/// </summary>
/// <remarks>
/// <para>
/// The game gates each level of a purchase on <c>prerequisitesPerLevel.Check(level)</c>, which takes
/// the level being bought and so cannot be published as a latched boolean the way the whole-entity
/// gate is. The collector publishes both the container's contents and the result of the native,
/// parameterized <c>Check(ConditionInfo)</c> call for the exact level the game would check next.
/// </para>
/// <para>
/// The native result is a differential oracle, not a replacement evaluator. Worker code still
/// evaluates the immutable rows, then compares its answer with this same-generation native verdict.
/// </para>
/// </remarks>
public sealed class WorldEntityRequirementTests : IDisposable
{
    public WorldEntityRequirementTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void AnUpgradesPerLevelResearchConditionIsPublished()
    {
        var scroll = Author(new global::UpgradeSO { maxLevel = 1 });
        var scribing = new global::ResearchSO();
        global::ResearchSO.All.Add(scribing);
        scroll.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        });

        var row = Single(Collect());

        Assert.Equal(scroll.GetGuid(), row.OwnerId);
        Assert.Equal(WorldRequirementOwnerKind.Upgrade, row.OwnerKind);
        Assert.Equal(0, row.Ordinal);
        Assert.Equal(WorldRequirementConditionKind.Research, row.Kind);
        Assert.Equal("ResearchRequirement", row.ConditionTypeName);
        Assert.Equal(scribing.GetGuid(), row.TargetId);
        Assert.Equal((int)Requirements.UpgradeRequirementType.AtLeast, row.ReqType);
        Assert.Equal(6d, row.BaseValue);
    }

    /// <summary>
    /// Structures carry the same container and are read by the same walk, so a build that starts
    /// authoring one is noticed rather than silently bought through. None ships today.
    /// </summary>
    [Fact]
    public void AStructuresPerLevelConditionIsPublishedAsItsOwnKindOfOwner()
    {
        var forge = new global::StructureSO();
        global::StructureSO.All.Add(forge);
        var quarry = new global::StructureSO();
        global::StructureSO.All.Add(quarry);
        forge.prerequisitesPerLevel.prerequisites.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 3d },
        });

        var world = Collect();

        Assert.True(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, forge.GetGuid(), out var start, out var count));
        Assert.Equal(1, count);

        var row = world.EntityRequirements[start];
        Assert.Equal(WorldRequirementOwnerKind.Structure, row.OwnerKind);
        Assert.Equal(WorldRequirementConditionKind.Structure, row.Kind);
        Assert.Equal(quarry.GetGuid(), row.TargetId);
        Assert.Equal(3d, row.BaseValue);
    }

    /// <summary>
    /// The common case, and it is a fact rather than a gap: an empty container's <c>Check</c> passes
    /// unconditionally, so an entity with no rows has nothing gating its next level.
    /// </summary>
    [Fact]
    public void AnEntityWithNoAuthoredConditionsPublishesNoRows()
    {
        Author(new global::UpgradeSO { maxLevel = 1 });

        var world = Collect();

        Assert.Equal(0, world.EntityRequirements.Count);
        Assert.False(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, global::UpgradeSO.All[0].GetGuid(), out _, out _));
    }

    [Fact]
    public void NativeParameterizedVerdictsUseTheExactUpgradeAndStructureCheckLevels()
    {
        var emptyUpgrade = Author(new global::UpgradeSO
        {
            level = 2,
            queuedLevels = 1,
            maxLevel = -1,
        });
        var gatedUpgrade = Author(new global::UpgradeSO
        {
            level = 4,
            queuedLevels = 2,
            maxLevel = -1,
        });
        gatedUpgrade.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.UnsupportedRequirement());
        var structure = new global::StructureSO { quantity = 7, queuedQuantity = 3 };
        global::StructureSO.All.Add(structure);

        var world = Collect();

        Assert.True(WorldRequirementNativeVerdictLookup.TryFind(
            world.RequirementNativeVerdicts, emptyUpgrade.GetGuid(), out var empty));
        Assert.Equal(WorldRequirementOwnerKind.Upgrade, empty.OwnerKind);
        Assert.Equal(4L, empty.CheckLevel);
        Assert.True(empty.Met);

        Assert.True(WorldRequirementNativeVerdictLookup.TryFind(
            world.RequirementNativeVerdicts, gatedUpgrade.GetGuid(), out var gated));
        Assert.Equal(7L, gated.CheckLevel);
        Assert.False(gated.Met);

        Assert.True(WorldRequirementNativeVerdictLookup.TryFind(
            world.RequirementNativeVerdicts, structure.GetGuid(), out var structureVerdict));
        Assert.Equal(WorldRequirementOwnerKind.Structure, structureVerdict.OwnerKind);
        Assert.Equal(7L, structureVerdict.CheckLevel);
        Assert.True(structureVerdict.Met);
    }

    [Fact]
    public void MissingParameterizedCheckMakesTheCategoryUnavailableBeforeCollection()
    {
        MissingCheckContainer.ParameterlessCalls = 0;
        var reader = new WorldEntityRequirementReader(
            typeof(MissingCheckUpgrade),
            typeof(MissingCheckStructure),
            typeof(global::ResearchSO),
            typeof(MissingCheckLink));

        Assert.False(reader.IsAvailable);
        var report = reader.Collect(
            new HashSet<Guid>(),
            new GameWorldCycleFrame { CollectedAtEpoch = 1 });

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains("Check(Requirements.ConditionInfo)", report.FirstFailure);
        Assert.Equal(0, MissingCheckContainer.ParameterlessCalls);
    }

    /// <summary>
    /// A threshold that grows with the level travels as the authored modifier, not as one number.
    /// </summary>
    /// <remarks>
    /// Exactly one upgrade in the shipped content has a non-zero one, and modelling the slot rather
    /// than that upgrade is what keeps a later build's second one from being read as flat.
    /// </remarks>
    [Fact]
    public void AThresholdThatGrowsWithTheLevelCarriesItsModifier()
    {
        var output = Author(new global::UpgradeSO { maxLevel = -1 });
        var casting = new global::IntVariable();
        global::IntVariable.All.Add(casting);
        output.prerequisitesPerLevel.prerequisites.Add(new Requirements.NumberRequirement
        {
            item = casting,
            reqType = Requirements.NumberRequirementType.Value,
            value = new Requirements.LeveledValue
            {
                baseValue = 1d,
                perLevel = new ValueModifier(ValueModifier.ValueModifierType.Raw, new BigDouble(1d)),
            },
        });

        var row = Single(Collect());

        Assert.Equal(WorldRequirementConditionKind.Number, row.Kind);
        Assert.Equal(1d, row.BaseValue);
        Assert.Equal((int)ValueModifier.ValueModifierType.Raw, row.PerLevel.ModifierType);
        Assert.Equal(new BigDouble(1d), row.PerLevel.Amount);
        Assert.Equal(0, row.PerLevel.Order);
        Assert.Equal(new BigDouble(0d), row.ModPerLevel.Amount);
    }

    /// <summary>
    /// A condition class this suite has not been audited against publishes a row saying so, rather
    /// than none. An entity with no rows reads as unconditional, which is exactly the wrong answer.
    /// </summary>
    [Fact]
    public void AConditionClassTheSuiteDoesNotModelPublishesAnUnknownRow()
    {
        var gated = Author(new global::UpgradeSO { maxLevel = 1 });
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UnsupportedRequirement());

        var row = Single(Collect());

        Assert.Equal(WorldRequirementConditionKind.Unknown, row.Kind);
        Assert.Equal("UnsupportedRequirement", row.ConditionTypeName);
        Assert.Equal(Guid.Empty, row.TargetId);
    }

    /// <summary>
    /// The unmodelled class is named once, where an operator will see it: the pass reports itself as
    /// incomplete and says which class it found. The reader runs once per lifecycle, so that is once
    /// per run of the game.
    /// </summary>
    [Fact]
    public void AnUnmodelledConditionClassIsNamedInThePassReport()
    {
        var gated = Author(new global::UpgradeSO { maxLevel = 1 });
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UnsupportedRequirement());

        var collector = new GameWorldCollector();
        var report = collector.Collect(new GameWorldCycleFrame { CollectedAtEpoch = 1 });
        var category = report.For("entity requirements");

        Assert.Equal(1, category.Sampled);
        Assert.Equal(1, category.Skipped);
        Assert.Contains("UnsupportedRequirement", category.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void NestedAndOrGroupsPreserveTheirAuthoredTree()
    {
        var gated = Author(new global::UpgradeSO { maxLevel = 1 });
        var scribing = new global::ResearchSO();
        global::ResearchSO.All.Add(scribing);
        var quarry = new global::StructureSO();
        global::StructureSO.All.Add(quarry);

        var outer = new Requirements.OrRequirement();
        outer.orConditions.Add(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        });
        var nested = new Requirements.AndRequirement();
        nested.andConditions.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 3d },
        });
        outer.orConditions.Add(nested);
        gated.prerequisitesPerLevel.prerequisites.Add(outer);

        var rows = Collect().EntityRequirements.AsSpan();

        Assert.Equal(4, rows.Length);
        Assert.Equal(WorldRequirementNodeKind.Group, rows[0].NodeKind);
        Assert.Equal(WorldRequirementOperator.Or, rows[0].Operator);
        Assert.Equal(-1, rows[0].ParentOrdinal);
        Assert.Equal(0, rows[0].Depth);
        Assert.Equal(WorldRequirementConditionKind.Research, rows[1].Kind);
        Assert.Equal(0, rows[1].ParentOrdinal);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal(WorldRequirementNodeKind.Group, rows[2].NodeKind);
        Assert.Equal(WorldRequirementOperator.And, rows[2].Operator);
        Assert.Equal(0, rows[2].ParentOrdinal);
        Assert.Equal(WorldRequirementConditionKind.Structure, rows[3].Kind);
        Assert.Equal(2, rows[3].ParentOrdinal);
        Assert.Equal(2, rows[3].Depth);
    }

    [Fact]
    public void EveryPrerequisiteLinkTierIsPublishedAsItsOwnOrderedContainer()
    {
        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        var scribing = new global::ResearchSO();
        global::ResearchSO.All.Add(scribing);

        var baseTier = new global::PrerequisiteLinkSO.LinkDefinition();
        baseTier.prerequisites.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 1d },
        });
        link.linkTiers.Add(baseTier);
        var secondTier = new global::PrerequisiteLinkSO.LinkDefinition();
        secondTier.prerequisites.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 7d },
        });
        link.linkTiers.Add(secondTier);

        var world = Collect();

        Assert.True(WorldEntityRequirementLookup.TryFindContainerRange(
            world.EntityRequirements, link.GetGuid(), 0, out var first, out var firstCount));
        Assert.True(WorldEntityRequirementLookup.TryFindContainerRange(
            world.EntityRequirements, link.GetGuid(), 1, out var second, out var secondCount));
        Assert.Equal(2, firstCount);
        Assert.Equal(2, secondCount);
        Assert.Equal(WorldRequirementOwnerKind.PrerequisiteLink,
            world.EntityRequirements[first].OwnerKind);
        Assert.Equal(WorldRequirementNodeKind.Group, world.EntityRequirements[first].NodeKind);
        Assert.Equal(WorldRequirementOperator.And, world.EntityRequirements[first].Operator);
        Assert.Equal(0, world.EntityRequirements[first + 1].ParentOrdinal);
        Assert.Equal(1d, world.EntityRequirements[first + 1].BaseValue);
        Assert.Equal(7d, world.EntityRequirements[second + 1].BaseValue);
    }

    /// <summary>
    /// One owner's conditions are contiguous and in the order it authored them, which is what makes
    /// the range lookup a binary search plus a forward walk.
    /// </summary>
    [Fact]
    public void OneOwnersConditionsStayContiguousAndInAuthoredOrder()
    {
        var first = Author(new global::UpgradeSO { maxLevel = 1 });
        var second = Author(new global::UpgradeSO { maxLevel = 1 });
        var research = new global::ResearchSO();
        global::ResearchSO.All.Add(research);

        second.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = first,
            reqType = Requirements.UpgradeRequirementType.OneLevel,
            value = new Requirements.LeveledValue(),
        });
        second.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = research,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 2d },
        });

        var world = Collect();

        Assert.True(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, second.GetGuid(), out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(WorldRequirementConditionKind.Upgrade, world.EntityRequirements[start].Kind);
        Assert.Equal(0, world.EntityRequirements[start].Ordinal);
        Assert.Equal(WorldRequirementConditionKind.Research, world.EntityRequirements[start + 1].Kind);
        Assert.Equal(1, world.EntityRequirements[start + 1].Ordinal);
    }

    private static global::UpgradeSO Author(global::UpgradeSO upgrade)
    {
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static WorldEntityRequirement Single(GameWorldState world)
    {
        Assert.Equal(1, world.EntityRequirements.Count);
        return world.EntityRequirements[0];
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::IntVariable.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::GameManager.currentFrame = 0;
    }

    private sealed class MissingCheckUpgrade
    {
        public MissingCheckUpgrade()
        {
            level = 0;
            queuedLevels = 0;
        }

        public static List<MissingCheckUpgrade> All { get; } = new();
        public MissingCheckContainer prerequisitesPerLevel = new();
        public int level;
        public int queuedLevels;
        public Guid GetGuid() => Guid.NewGuid();
        public int GetPurchaseLevel() => level;
    }

    private sealed class MissingCheckStructure
    {
        public MissingCheckStructure() => quantity = 0;

        public static List<MissingCheckStructure> All { get; } = new();
        public MissingCheckContainer prerequisitesPerLevel = new();
        public int quantity;
        public Guid GetGuid() => Guid.NewGuid();
    }

    private sealed class MissingCheckLink
    {
        public sealed class LinkDefinition
        {
            public MissingCheckContainer prerequisites = new();
        }

        public static List<MissingCheckLink> All { get; } = new();
        public List<LinkDefinition> linkTiers = new();
        public Guid GetGuid() => Guid.NewGuid();
    }

    private sealed class MissingCheckContainer
    {
        internal static int ParameterlessCalls;
        public List<object> prerequisites = new();
        public bool Check()
        {
            ParameterlessCalls++;
            return true;
        }
    }
}
