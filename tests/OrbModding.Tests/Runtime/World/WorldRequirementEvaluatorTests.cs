using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The published condition rows answer <c>prerequisitesPerLevel.Check(level)</c> without the game.
/// </summary>
/// <remarks>
/// <para>
/// Every case authors real registry entries, runs the real collector, and evaluates what it published.
/// A hand-built table would agree with whatever the evaluator expected and prove nothing about the two
/// halves meaning the same thing — and the halves are the risk here, since the reader publishes a
/// comparison as an integer and the evaluator decides what that integer means.
/// </para>
/// <para>
/// The numbers a condition is compared against are the game's, not the suite's convenience: a research
/// entry's level is three terms, and a structure is checked at the quantity it already has rather than
/// the one it is buying.
/// </para>
/// </remarks>
public sealed class WorldRequirementEvaluatorTests : IDisposable
{
    public WorldRequirementEvaluatorTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    /// <summary>
    /// The common case. An empty container passes unconditionally in the game, so an owner with no
    /// published rows is admitted rather than treated as unreadable.
    /// </summary>
    [Fact]
    public void AnOwnerWithNoAuthoredConditionsIsMet()
    {
        var upgrade = Upgrade();

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), upgrade.GetGuid(), 1));
    }

    /// <summary>
    /// The live case this whole model exists for: an upgrade gated on a research entry reaching level
    /// six. Below it the purchase is never planned; at it, it is.
    /// </summary>
    [Fact]
    public void AResearchThresholdRefusesUntilTheResearchReachesIt()
    {
        var scroll = Upgrade();
        var scribing = Research();
        RequireResearch(scroll, scribing, 6d);

        scribing.level = 5;
        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));

        scribing.level = 6;
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));
    }

    /// <summary>
    /// A research entry's level is what it bought plus what it was granted, in two separate records.
    /// Comparing the bought field alone would refuse purchases the game allows, for every entry
    /// carrying a bonus — which is most of them by the mid game.
    /// </summary>
    [Fact]
    public void AResearchLevelIsTheSumOfItsThreeTerms()
    {
        var scroll = Upgrade();
        var scribing = Research();
        RequireResearch(scroll, scribing, 6d);

        scribing.level = 3;
        scribing.baseLevels = new ValueModifierRecord(new BigDouble(2d, 0));
        scribing.bonusLevels = new ValueModifierRecord(new BigDouble(1d, 0));

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));
    }

    /// <summary>
    /// The threshold moves with the level being bought, so the same condition admits one level and
    /// refuses the next.
    /// </summary>
    [Fact]
    public void AThresholdThatScalesIsCheckedAtTheLevelBeingBought()
    {
        var output = Upgrade();
        var casting = new global::IntVariable { Value = 3 };
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

        var world = Collect();

        // baseValue one, rising by one a level: three at level two, four at level three.
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, output.GetGuid(), 2));
        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(world, output.GetGuid(), 3));
    }

    /// <summary>
    /// An upgrade is checked at the level a purchase made now would reach, counting levels already in
    /// flight. Checking the owned level would let a second purchase be planned against the first one's
    /// requirements.
    /// </summary>
    [Fact]
    public void AnUpgradeIsCheckedAtTheLevelItsNextPurchaseReaches()
    {
        var upgrade = Upgrade();
        upgrade.level = 2;
        upgrade.queuedLevels = 1;

        var world = Collect();
        Assert.True(WorldLookup.TryFind(world.Upgrades, upgrade.GetGuid(), out var published));

        Assert.Equal(4L, WorldRequirementEvaluator.UpgradeCheckLevel(in published));
    }

    /// <summary>
    /// A structure is checked at the quantity it already has, which is one less than the symmetry with
    /// upgrades would suggest. The game's two call sites genuinely differ, and reproducing the tidier
    /// one would shift every structure's gate by a level.
    /// </summary>
    [Fact]
    public void AStructureIsCheckedAtTheQuantityItAlreadyHas()
    {
        var forge = new global::StructureSO { quantity = 3, queuedQuantity = 1 };
        global::StructureSO.All.Add(forge);

        var world = Collect();
        Assert.True(WorldLookup.TryFind(world.Structures, forge.GetGuid(), out var published));

        Assert.Equal(3L, WorldRequirementEvaluator.StructureCheckLevel(in published));
    }

    /// <summary>Every condition class the shipped content authors, evaluated against its own registry.</summary>
    /// <remarks>
    /// One case rather than eight because what is under test is the join: that the kind the reader
    /// classified, the comparison integer it copied, and the published fact the evaluator reaches for
    /// all line up. A per-kind file would pass with the same three things wired to each other wrongly.
    /// </remarks>
    [Fact]
    public void EveryModelledConditionKindReadsItsOwnPublishedFact()
    {
        var gated = Upgrade();

        var prior = Upgrade();
        prior.level = 1;
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = prior,
            reqType = Requirements.UpgradeRequirementType.OneLevel,
            value = new Requirements.LeveledValue(),
        });

        var scribing = Research();
        scribing.level = 6;
        RequireResearch(gated, scribing, 6d);

        var quarry = new global::StructureSO { quantity = 4 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 4d },
        });

        var bolt = new global::SpellRecipeSO { masteryLevel = 2, discovered = true };
        global::SpellRecipeSO.All.Add(bolt);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.SpellRequirement
        {
            item = bolt,
            reqType = Requirements.SpellRequirementType.MasteryLevel,
            value = new Requirements.LeveledValue { baseValue = 2d },
        });

        var brew = new global::AlchemyRecipeSO { advancementLevel = 3, discovered = true };
        global::AlchemyRecipeSO.All.Add(brew);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.AlchemyRecipeRequirement
        {
            item = brew,
            reqType = Requirements.AlchemyRecipeType.AdvLevel,
            value = new Requirements.LeveledValue { baseValue = 3d },
        });

        var rite = new global::RitualSO { reachedLevel = 5, discovered = true };
        global::RitualSO.All.Add(rite);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.RitualRequirement
        {
            item = rite,
            reqType = Requirements.RitualRequirementType.ReachedLevel,
            value = new Requirements.LeveledValue { baseValue = 5d },
        });

        var casting = new global::IntVariable { Value = 9 };
        global::IntVariable.All.Add(casting);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.NumberRequirement
        {
            item = casting,
            reqType = Requirements.NumberRequirementType.Value,
            value = new Requirements.LeveledValue { baseValue = 9d },
        });

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>Every condition has to hold, so one failure among six is a refusal.</summary>
    [Fact]
    public void OneUnmetConditionRefusesTheWholeOwner()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 6;
        RequireResearch(gated, scribing, 6d);

        var quarry = new global::StructureSO { quantity = 1 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 4d },
        });

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A condition class nobody has modelled refuses its owner, and says so as its own verdict rather
    /// than as an ordinary failure. Both refuse the purchase; only one of them is this suite's problem
    /// to fix.
    /// </summary>
    [Fact]
    public void AnUnmodelledConditionClassIsUnevaluableRatherThanUnmet()
    {
        var gated = Upgrade();
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.OrRequirement());

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The comparisons that ask another entity for its whole-entity gate reach a <c>Check()</c> that
    /// writes, so they are refused rather than approximated. <c>Visible</c> is the one the shipped
    /// content actually authors.
    /// </summary>
    [Fact]
    public void AComparisonThatWouldHaveToWriteIsUnevaluable()
    {
        var gated = Upgrade();
        var prior = Upgrade();
        prior.level = 3;
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = prior,
            reqType = Requirements.UpgradeRequirementType.Visible,
            value = new Requirements.LeveledValue(),
        });

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A condition pointing at something the snapshot does not hold is refused rather than read as an
    /// entity at level nought, which would refuse silently and for the wrong reason — or, for a
    /// zero threshold, admit.
    /// </summary>
    [Fact]
    public void AConditionWhoseTargetIsMissingIsUnevaluable()
    {
        var gated = Upgrade();
        var scribing = Research();
        RequireResearch(gated, scribing, 0d);
        global::ResearchSO.All.Clear();

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// An unevaluable condition outranks an unmet one. The purchase is refused either way; the verdict
    /// exists so that whatever narrates the refusal names the suite's gap rather than the save's
    /// progress.
    /// </summary>
    [Fact]
    public void AnUnevaluableConditionOutranksAnUnmetOne()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 1;
        RequireResearch(gated, scribing, 6d);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.OrRequirement());

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The generic condition points at an arbitrary upgradeable object and asks for its level, which is
    /// a different expression per target type. Only a number variable's is modelled, so a generic
    /// condition on anything else is refused rather than answered from the wrong override.
    /// </summary>
    /// <remarks>
    /// The modelled half needs a target that is both an upgradeable object and a registered number
    /// variable, which the game has and these stubs do not — the stub registries model the two number
    /// registries without the shared base. The live differential pass is what covers it.
    /// </remarks>
    [Fact]
    public void AGenericConditionOnSomethingThatIsNotANumberVariableIsUnevaluable()
    {
        var gated = Upgrade();
        var quarry = new global::StructureSO { quantity = 9 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.GenericRequirement
        {
            item = quarry,
            reqType = Requirements.GenericRequirementType.Level,
            value = new Requirements.LeveledValue { baseValue = 1d },
        });

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    private static global::UpgradeSO Upgrade()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static global::ResearchSO Research()
    {
        var research = new global::ResearchSO { maxLevel = 10 };
        global::ResearchSO.All.Add(research);
        return research;
    }

    private static void RequireResearch(
        global::UpgradeSO owner, global::ResearchSO target, double threshold) =>
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = threshold },
        });

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::RitualSO.All.Clear();
        global::IntVariable.All.Clear();
    }
}
