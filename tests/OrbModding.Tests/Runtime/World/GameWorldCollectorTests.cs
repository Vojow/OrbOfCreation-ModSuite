using System;
using System.Collections.Generic;
using OrbAutomata;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The collector is the one place the suite touches the game for world state, so its failure
/// behaviour matters more than its happy path. These tests pin that a shortfall is always visible: a
/// category that cannot be read says so, an entity that cannot be read costs one row, and nothing
/// ever reports a clean pass it did not achieve.
/// </summary>
/// <remarks>
/// Most tests drive purpose-built types rather than the shared game stubs. Those types spell out the
/// exact member shape each binder requires, which makes this file a readable statement of the
/// contract, and they keep their registries private so no other test class can be perturbed by one
/// running here. One test does bind against the stubs, to prove the by-name resolution path works
/// end to end; the real member names are validated separately against the shipped assemblies by the
/// native contract manifest.
/// </remarks>
public sealed class GameWorldCollectorTests : IDisposable
{
    public GameWorldCollectorTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    private static void ClearRegistries()
    {
        FakeResource.All.Clear();
        FakeStructure.All.Clear();
        FakeUpgrade.All.Clear();
        FakeResearch.All.Clear();
        FakeNumber.All.Clear();
        FakeCount.All.Clear();
        FakeFlag.All.Clear();
        FakePlayerGlobals.Reset();
        WorldCategoryFakes.Clear();
    }

    private static readonly Dictionary<string, Type?> Defaults = Build();

    /// <summary>
    /// Every category the collector walks, resolved to a stand-in. The categories these tests are
    /// about get purpose-built types spelled out below; the rest come from
    /// <see cref="WorldCategoryFakes"/>, so a complete pass really is complete.
    /// </summary>
    private static Dictionary<string, Type?> Build()
    {
        var byName = new Dictionary<string, Type?>(StringComparer.Ordinal)
        {
            ["ResourceSO"] = typeof(FakeResource),
            ["StructureSO"] = typeof(FakeStructure),
            ["UpgradeSO"] = typeof(FakeUpgrade),
            ["ResearchSO"] = typeof(FakeResearch),
            ["DoubleVariable"] = typeof(FakeNumber),
            ["IntVariable"] = typeof(FakeCount),
            ["BoolVariable"] = typeof(FakeFlag),

            // Not categories: the frame-wide globals reader resolves these two by name, and a
            // collector that cannot reach them silently prices every structure at parity.
            ["Player"] = typeof(FakePlayerGlobals),
            ["ValueModifierRecord"] = typeof(FakeModifierRecord),
        };

        foreach (var pair in WorldCategoryFakes.ByTypeName) byName[pair.Key] = pair.Value;
        return byName;
    }

    /// <summary>
    /// A collector over the purpose-built types. Overrides are keyed by the game type name the binder
    /// asks for, so a test can say "this category resolves to something else" or "this category
    /// resolves to nothing" — the latter being a distinct case the collector must report differently.
    /// </summary>
    private static GameWorldCollector Collector(params (string TypeName, Type? Type)[] overrides)
    {
        var byName = new Dictionary<string, Type?>(Defaults, StringComparer.Ordinal);
        foreach (var (typeName, type) in overrides) byName[typeName] = type;
        return new GameWorldCollector(name => byName.TryGetValue(name, out var type) ? type : null);
    }

    [Fact]
    public void OnePassReadsEveryCategory()
    {
        var mana = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var scholar = Guid.NewGuid();
        var alchemy = Guid.NewGuid();
        var theory = Guid.NewGuid();
        var grimoire = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new FakeModifierRecord(100d),
            Rate = 2.5d,
            Visible = true,
        });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            structureType = new FakeReferencedEntity { Identity = scholar },
            Level = 12,
            Queued = 3,
            Available = true,
        });
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = alchemy, Level = 1, maxLevel = 1, Available = false });
        FakeResearch.All.Add(new FakeResearch { Identity = theory, level = 4, isDeveloping = true, Available = true });
        FakeRecipeBook.All.Add(new FakeRecipeBook { Identity = grimoire, Available = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(5, report.TotalSampled);

        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var resource));
        Assert.Equal(60d, resource.Reading.Quantity.ToDouble());
        Assert.Equal(100d, resource.Reading.Capacity.ToDouble());
        Assert.Equal(2.5d, resource.Reading.Rate.ToDouble());
        Assert.True(resource.Reading.Visible);

        Assert.True(WorldLookup.TryFind(world.Structures, cauldron, out var structure));
        Assert.Equal(12d, structure.Reading.Level.ToDouble());
        Assert.Equal(3d, structure.Reading.QueuedLevels.ToDouble());
        Assert.True(structure.Reading.Unlocked);
        Assert.Equal(scholar, structure.Reading.StructureTypeId);

        Assert.True(WorldLookup.TryFind(world.Upgrades, alchemy, out var upgrade));
        Assert.Equal(1, upgrade.Reading.Level);
        Assert.Equal(1, upgrade.Reading.MaxLevel);
        Assert.False(upgrade.Reading.Available);

        Assert.True(WorldLookup.TryFind(world.Research, theory, out var research));
        Assert.Equal(4, research.Level);
        Assert.True(research.IsDeveloping);
        Assert.True(research.Available);

        Assert.True(WorldLookup.TryFind(world.RecipeBooks, grimoire, out var recipeBook));
        Assert.True(recipeBook.Available);
    }

    [Fact]
    public void AStructuresDisabledEffectFlagIsPublished()
    {
        var active = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        FakeStructure.All.Add(new FakeStructure { Identity = active, disabled = false });
        FakeStructure.All.Add(new FakeStructure { Identity = disabled, disabled = true });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Structures, active, out var activeRow));
        Assert.False(activeRow.Reading.Disabled);
        Assert.True(WorldLookup.TryFind(world.Structures, disabled, out var disabledRow));
        Assert.True(disabledRow.Reading.Disabled);
    }

    [Fact]
    public void EveryCategoryTheGamePersistsStateForIsWalked()
    {
        // The scope claim, asserted rather than described. Forty-four passes: the four categories
        // the suite started with, four global-variable registries, twenty-six more the game persists
        // per-entity state for, the harvest elements' own resources — which are not in the resource
        // registry and would otherwise be reachable from nothing — the structure and upgrade cost
        // lists, the authored effects, each plot's authoring and each action's completion blocks,
        // each purchasable entity's per-level conditions, which are second walks of registries rather
        // than registries of their own, the plot-and-action pairs, which belong to neither side, and
        // the two that belong to no per-type registry at all and are reached by uuid: the action
        // queues, the equipped spell loadout, and the paired Concept registries. A pass that quietly stopped covering one would show
        // up only as a consumer finding nothing where there was something.
        var report = Collector().Collect();

        Assert.Equal(44, report.Categories.Length);
        Assert.True(report.IsComplete, report.Describe());

        // A few named explicitly, one per shape: a mastery track, a state machine, a lone flag, and a
        // levelled grouping type.
        foreach (var category in
                 new[] { "resources", "harvest resources", "time runes", "challenges", "views", "resource types", "recipe books", "modifier variables", "structure costs", "upgrade costs", "plot actions", "action queues", "spell slots", "concept instances", "plot authoring", "effect blocks", "entity requirements" })
        {
            Assert.Equal(WorldCategoryOutcome.Collected, report.For(category).Outcome);
        }
    }

    [Fact]
    public void ACategoryWithNothingToShowIsStillWalked()
    {
        // Empty is a fact about the save, not about the read, and the two must stay distinguishable.
        // A category that read cleanly and found nothing is complete; one that could not be read is
        // not, and only the latter should ever make a consumer doubt its own emptiness.
        var report = Collector().Collect();
        var glyphs = report.For("glyphs");

        Assert.Equal(WorldCategoryOutcome.Collected, glyphs.Outcome);
        Assert.Equal(0, glyphs.Sampled);
        Assert.True(glyphs.IsClean);
        Assert.Empty(glyphs.FirstFailure);
    }

    [Fact]
    public void AStateEnumTravelsAsItsUnderlyingInteger()
    {
        // The suite deliberately does not mirror the game's enums. A copied enum would keep compiling
        // against a build that inserted a member in the middle, and every comparison against it would
        // silently start meaning something else. The integer is what the game persists.
        var challenge = Guid.NewGuid();
        FakeChallenge.All.Add(new FakeChallenge
        {
            Identity = challenge,
            level = 3,
            state = FakeState.Done,
            hasBeenSeen = true,
            rewardQueued = true,
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Challenges, challenge, out var row));
        Assert.Equal((int)FakeState.Done, row.State);
        Assert.Equal(3, row.Level);
        Assert.True(row.Seen);
        Assert.True(row.RewardQueued);
    }

    [Fact]
    public void ASpellsMasteryReadinessIsCollectedFromTheGamesOwnAnswer()
    {
        // The one fact on a spell recipe that is not a field. Its threshold lives in a container the
        // snapshot does not publish, so MasteryXp has nothing to be compared against and the game's
        // own predicate is the only readable form of "this level can be bought". See W59.
        var ready = Guid.NewGuid();
        var banking = Guid.NewGuid();
        FakeSpellRecipe.All.Add(new FakeSpellRecipe
        {
            Identity = ready,
            discovered = true,
            masteryLevel = 4,
            readyToLevel = true,
        });
        FakeSpellRecipe.All.Add(new FakeSpellRecipe
        {
            Identity = banking,
            discovered = true,
            masteryLevel = 4,
            readyToLevel = false,
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.SpellRecipes, ready, out var readyRow));
        Assert.True(readyRow.MasteryLevelReady);
        Assert.True(WorldLookup.TryFind(world.SpellRecipes, banking, out var bankingRow));
        Assert.False(bankingRow.MasteryLevelReady);

        // Same mastery level on both: readiness is its own fact, not one the level implies.
        Assert.Equal(readyRow.MasteryLevel, bankingRow.MasteryLevel);
    }

    [Fact]
    public void ConceptRecipesInstancesAndDrainVectorsArePublishedTogether()
    {
        var resource = Guid.NewGuid();
        var coreType = new FakeAlchemyType { Identity = Guid.NewGuid() };
        var recipe = new FakeAlchemyRecipe
        {
            Identity = Guid.NewGuid(),
            coreType = coreType,
            drainCost = new FakeSpellCostList().With(resource, 7d),
        };
        FakeAlchemyRecipe.All.Add(recipe);

        var recipes = new FakeAlchemyRecipeList();
        recipes.value.Add(recipe);
        var instances = new FakeAlchemyInstanceList();
        instances.value.Add(new FakeAlchemyInstance(recipe)
        {
            quantity = 2,
            queuedQuantity = 3,
            resourceDrain = new FakeAlchemyDrain
            {
                ratio = new BigDouble(0.75d),
                current = new FakeSpellCostList().With(resource, 11d),
            },
        });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldConceptRecipeLookup.TryFind(world.ConceptRecipes, recipe.Identity, out var concept));
        Assert.Equal(coreType.Identity, concept.CoreTypeId);
        Assert.True(concept.CanAddNow);
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, recipe.Identity, out var alchemyRecipe));
        Assert.Equal(coreType.Identity, alchemyRecipe.CoreTypeId);

        Assert.True(WorldAlchemyInstanceLookup.TryFind(world.AlchemyInstances, recipe.Identity, out var instance));
        Assert.Equal(2, instance.Quantity);
        Assert.Equal(3, instance.QueuedQuantity);
        Assert.False(instance.IsSettled);
        Assert.True(instance.DrainReadable);
        Assert.Equal(0.75d, instance.DrainRatio.ToDouble());

        Assert.True(WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipe.Identity,
            WorldAlchemyCostKind.RecipeDrain,
            out var recipeStart,
            out var recipeCount));
        Assert.Equal(1, recipeCount);
        Assert.Equal(resource, world.AlchemyCosts[recipeStart].ResourceId);
        Assert.Equal(7d, world.AlchemyCosts[recipeStart].Amount.ToDouble());

        Assert.True(WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipe.Identity,
            WorldAlchemyCostKind.CurrentDrain,
            out var currentStart,
            out var currentCount));
        Assert.Equal(1, currentCount);
        Assert.Equal(11d, world.AlchemyCosts[currentStart].Amount.ToDouble());
    }

    [Fact]
    public void AViewsComposedAvailabilityIsPublished()
    {
        var unlocked = Guid.NewGuid();
        var locked = Guid.NewGuid();
        FakeView.All.Add(new FakeView
        {
            Identity = unlocked,
            active = true,
        });
        FakeView.All.Add(new FakeView
        {
            Identity = locked,
            active = false,
            alwaysActive = false,
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Views, unlocked, out var unlockedView));
        Assert.True(unlockedView.Available);
        Assert.True(WorldLookup.TryFind(world.Views, locked, out var lockedView));
        Assert.False(lockedView.Available);
    }

    [Fact]
    public void TheGlobalVariableRegistriesAreCollectedAsRegistries()
    {
        // Player and GlobalVariables expose well over a hundred accessors between them, and every one
        // is a lookup into one of these lists. Walking the lists collects the lot without the
        // suite declaring — and then having to keep declaring — a contract per accessor.
        var multiBuy = Guid.NewGuid();
        var costScaling = Guid.NewGuid();
        var offlineProgress = Guid.NewGuid();

        FakeCount.All.Add(new FakeCount { Identity = multiBuy, value = new FakeModifierRecord(3d) });
        FakeNumber.All.Add(new FakeNumber
        {
            Identity = costScaling,
            value = new FakeModifierRecord(150d),
            isPercentVariable = true,
        });
        FakeFlag.All.Add(new FakeFlag { Identity = offlineProgress, value = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        // The two number registries stay separate tables, because the game keeps them separate and a
        // caller asking for a count wants the one the game converts to an integer.
        Assert.True(WorldLookup.TryFind(world.IntVariables, multiBuy, out var buy));
        Assert.Equal(3d, buy.Value.ToDouble());
        Assert.False(buy.IsPercent);
        Assert.False(WorldLookup.TryFind(world.DoubleVariables, multiBuy, out _));

        Assert.True(WorldLookup.TryFind(world.DoubleVariables, costScaling, out var scaling));
        Assert.Equal(150d, scaling.Value.ToDouble());

        // Not presentation: a percent variable holds 100 for parity, so a consumer reading one as a
        // plain number is out by two orders of magnitude.
        Assert.True(scaling.IsPercent);

        Assert.True(WorldLookup.TryFind(world.BoolVariables, offlineProgress, out var flag));
        Assert.True(flag.Value);
    }

    [Fact]
    public void TheModifierRegistryTravelsAsArithmeticRatherThanAsAReference()
    {
        // A structure's costPerQuantity does not hold a modifier, it holds a reference to one of
        // these. Collecting the registry is what lets an entity row carry an identity instead of a
        // copy, and lets one modifier shared by twenty structures be read once.
        var perQuantity = Guid.NewGuid();
        FakeModifierVariable.All.Add(new FakeModifierVariable
        {
            Identity = perQuantity,
            value = new FakeValueModifier(FakeModifierKind.MultiStacking, 1.15d, order: 2),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ModifierVariables, perQuantity, out var row));

        // The enum travels as its integer, for the reason every other game enum does.
        Assert.Equal((int)FakeModifierKind.MultiStacking, row.ModifierType);
        Assert.Equal(1.15d, row.Amount.ToDouble(), 10);

        // Order is not decoration: modifiers sharing an order merge with each other before any of
        // them is applied, so dropping it changes the arithmetic rather than the presentation.
        Assert.Equal(2, row.Order);
    }

    [Fact]
    public void AnUncappedResourceKeepsTheGamesNegativeSentinel()
    {
        // The collector must not normalise the game's "no ceiling" marker into something friendlier.
        // Negative is what HasMaxQuantity() tests for, and the deriver reads the same convention.
        FakeResource.All.Add(new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 1e9d,
            maxQuantity = new FakeModifierRecord(-1d),
        });

        var collector = Collector();
        collector.Collect();

        var resource = collector.Build().Resources[0];
        Assert.True(resource.Reading.Capacity.ToDouble() < 0d);
        Assert.False(resource.IsCapped);
    }

    [Fact]
    public void CollectingNeverAsksTheGameToRecalculateACachedValue()
    {
        // The load-bearing property of the whole collector. ValueModifierRecord.GetValue() is not a
        // read: on a dirty record it runs an allocating pass over both modifier dictionaries and
        // writes calculatedValue, calculationDirty, observedId, and updateObservers. Calling it would
        // make the game recompute and re-stamp its own observable at whatever point in the frame the
        // suite's pump happens to run, which is a mutation the suite has no business performing
        // outside the action boundary.
        //
        // The collector reproduces GetValue() from the record's own fields instead — its memo when it
        // is clean, its base value and modifier sets when it is dirty — which asks the game nothing
        // and still answers what GetValue() would have.
        var record = new FakeModifierRecord(250d);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        var report = collector.Collect();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingIgnoresACacheTheGameHasNotFilled()
    {
        // The cycle-1 defect, at the level it was introduced. calculatedValue is [NonSerialized], so
        // a record comes back from a save at zero, and one that is dirty with it is one the game will
        // recompute the moment anything asks. Publishing that zero is what priced 180 structures at
        // nothing.
        var record = new FakeModifierRecord(250d).WithStaleCache(0d).Dirty();
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    /// <summary>
    /// The other half of the same rule, and the one that cost every structure price. A clean record's
    /// memo is not a stale number the game is about to replace — nothing will dirty it, so nothing
    /// will recompute it, and the memo is what the game charges from for the rest of the session.
    /// Publishing a recomputation instead would be publishing a number the game never uses.
    /// </summary>
    [Fact]
    public void CollectingReadsTheMemoTheGameWillNotRecompute()
    {
        var record = new FakeModifierRecord(250d).WithStaleCache(0d);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.False(record.IsCalculationDirty);
        Assert.Equal(0d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingFoldsTheModifiersOnARecordRatherThanReadingItsValue()
    {
        var record = new FakeModifierRecord(100d).WithStaleCache(999d).Dirty();
        record.passiveModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.Raw, new BigDouble(50d));
        record.activeModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.MultiStacking, new BigDouble(2d), order: 1);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        // (100 + 50) * 2 = 300 — both sets, lowest order first, and nothing from the cache.
        Assert.Equal(300d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void TheGameTypesBindThroughTheDefaultConstructor()
    {
        // Resolution by name is how this reaches the real assemblies, and the stubs carry the same
        // member names. If a member a binder needs disappears, this is the cheap early warning; the
        // shipped assemblies are checked separately by the contract manifest.
        var collector = new GameWorldCollector();
        var report = collector.Collect();

        Assert.True(collector.IsFullyAvailable, report.Describe());
    }

    [Fact]
    public void AMissingCategoryDegradesOnlyItself()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid() });
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.NewGuid() });
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = Guid.NewGuid() });

        // A type that exists but exposes none of the members research needs.
        var collector = Collector(("ResearchSO", typeof(void)));
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(collector.IsFullyAvailable);
        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("research").Outcome);

        // The other three are untouched by the gap.
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(1, world.Structures.Count);
        Assert.Equal(1, world.Upgrades.Count);
        Assert.Equal(0, world.Research.Count);

        var described = report.Describe();
        Assert.Contains("research", described, StringComparison.Ordinal);
        Assert.DoesNotContain("resources", described, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentTypeIsReportedAsSuchRatherThanAsAnEmptyCategory()
    {
        // "The type is gone" and "the type is here but a member moved" call for different fixes, so
        // the report has to tell them apart.
        var report = Collector(("ResearchSO", null)).Collect();

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("research").Outcome);
        Assert.Contains(
            "ResearchSO type was not found", report.For("research").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingMemberNamesTheMemberItCouldNotBind()
    {
        // Diagnosing a game update means knowing which member moved, not just that something did.
        var report = Collector(("ResourceSO", typeof(PartialResource))).Collect();
        var resources = report.For("resources");

        Assert.Equal(WorldCategoryOutcome.Unavailable, resources.Outcome);
        Assert.Contains("GetTrueRate", resources.FirstFailure, StringComparison.Ordinal);
        Assert.Contains("IsVisible", resources.FirstFailure, StringComparison.Ordinal);

        // A member that is present must not be blamed.
        Assert.DoesNotContain("GetQuantity", resources.FirstFailure, StringComparison.Ordinal);

        // A record read is blamed on the base value inside it, because the record is usually what is
        // missing rather than the whole entity — PartialResource still has maxQuantity, just not a
        // record named quality.
        Assert.Contains("quality.baseValue", resources.FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ACategoryTheCollectorNeverWalkedAnswersAsUnavailable()
    {
        // Asking about a category that is not in the pass is a caller mistake worth an answer rather
        // than an exception — the report is a diagnostic, and one that threw would be consulted less.
        var absent = Collector().Collect().For("weather");

        Assert.Equal(WorldCategoryOutcome.Unavailable, absent.Outcome);
        Assert.Contains("not collected", absent.FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedIdentityCostsOneRowRatherThanTheSnapshot()
    {
        // Table construction treats a duplicate as a hard authoring error, so the collector has to
        // catch it first — otherwise one bad registry entry throws away every other reading in the
        // pass.
        var shared = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource { Identity = shared, Quantity = 1d });
        FakeResource.All.Add(new FakeResource { Identity = shared, Quantity = 2d });

        var collector = Collector();
        var report = collector.Collect();
        var resources = report.For("resources");

        Assert.Equal(1, resources.Sampled);
        Assert.Equal(1, resources.Skipped);
        Assert.Contains(shared.ToString(), resources.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.IsComplete);

        // The first reading wins; the pass does not silently prefer the last writer.
        var world = collector.Build();
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(1d, world.Resources[0].Reading.Quantity.ToDouble());
    }

    [Fact]
    public void AnIdentityCollisionAcrossCategoriesIsAlsoRejected()
    {
        // The game keys every entity in one UUID space, so a structure sharing a resource's identity
        // means the game's own RuntimeLookup is already broken. Accepting it would let a lookup
        // return whichever row sorted first.
        var shared = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource { Identity = shared });
        FakeStructure.All.Add(new FakeStructure { Identity = shared });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(0, world.Structures.Count);
        Assert.Equal(1, report.For("structures").Skipped);
    }

    [Fact]
    public void AnUnidentifiedEntityIsSkipped()
    {
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.Empty });
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Structures.Count);
        Assert.Equal(1, report.For("structures").Skipped);
        Assert.Contains("empty identity", report.For("structures").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullRegistryEntryIsSkipped()
    {
        FakeUpgrade.All.Add(null!);
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Upgrades.Count);
        Assert.Equal(1, report.For("upgrades").Skipped);
    }

    [Fact]
    public void AnEntityWhoseAccessorThrowsCostsOneRow()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), ThrowOnRate = true });
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), Quantity = 7d });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(7d, world.Resources[0].Reading.Quantity.ToDouble());
        Assert.Equal(1, report.For("resources").Skipped);
        Assert.Contains("rate unavailable", report.For("resources").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectingTwiceReplacesTheReadingRatherThanAppendingToIt()
    {
        // The buffers are reused across collections. A missed reset would grow the snapshot every
        // cycle and immediately trip the duplicate check in table construction.
        var mana = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource { Identity = mana, Quantity = 1d });

        var harvest = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(harvest);
        FakePlotNode.All.Add(new FakePlotNode().With(FakePlotPhase.Idle, 1).Offering(harvest));

        var collector = Collector();

        collector.Collect();
        Assert.Equal(1, collector.Build().Resources.Count);

        FakeResource.All[0].Quantity = 99d;
        collector.Collect();

        var world = collector.Build();
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(99d, world.Resources[0].Reading.Quantity.ToDouble());

        // The pair table has no identity to collide on, and its own reader folds a repeat into the
        // row already there, so a missed reset would not even change the row count — it would quietly
        // report the plot offering the action once more on every cycle it survived.
        Assert.Equal(1, world.PlotActions.Count);
        Assert.True(WorldPlotActionLookup.TryFind(
            world.PlotActions, FakePlotNode.All[0].Identity, harvest.Identity, out var pair));
        Assert.Equal(1, pair.Reading.OfferedCount);
    }

    [Fact]
    public void APassBecomesASnapshotEveryConsumerCanLookUp()
    {
        // The end-to-end shape: main thread grabs, worker derives, consumers binary-search. This is
        // the substitution the whole design exists to make possible.
        var mana = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new FakeModifierRecord(100d),
        });
        FakeStructure.All.Add(new FakeStructure { Identity = cauldron, Level = 4, Queued = 1 });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var resource));
        Assert.True(resource.IsCapped);
        Assert.Equal(40d, resource.Headroom.ToDouble());

        Assert.True(WorldLookup.TryFind(world.Structures, cauldron, out var structure));
        Assert.Equal(5d, structure.CommittedLevel.ToDouble());

        Assert.False(WorldLookup.TryFind(world.Upgrades, Guid.NewGuid(), out _));
    }

    [Fact]
    public void APassGrowsPastItsInitialBufferWithoutLosingReadings()
    {
        var identities = new Guid[200];
        for (var index = 0; index < identities.Length; index++)
        {
            identities[index] = Guid.NewGuid();
            FakeStructure.All.Add(new FakeStructure { Identity = identities[index], Level = index });
        }

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(200, world.Structures.Count);
        Assert.Equal(200, report.For("structures").Sampled);

        // Rows are ordered by identity rather than by registry position, so every reading is checked
        // through the lookup that consumers actually use.
        for (var index = 0; index < identities.Length; index++)
        {
            Assert.True(WorldLookup.TryFind(world.Structures, identities[index], out var structure));
            Assert.Equal(index, (int)structure.Reading.Level.ToDouble());
        }
    }

    // The member shape each binder requires, stated once. Field names that a binder reads as fields
    // are spelled exactly as the game spells them; everything else is reached through an accessor, so
    // the property names here are free to read well.
    private sealed class FakeStructure
    {
        public static readonly List<FakeStructure> All = new();

        public Guid Identity = Guid.NewGuid();
        public FakeReferencedEntity structureType = new();
        public int Level;
        public int Queued;
        public bool Available = true;
        public int queuedEchos;
        public int completedEchos;
        public int selfBonusLevels;
        public BigDouble queueTimeLeft;
        public BigDouble currentBuildTime;
        public bool flagged;
        public int baseLevel;
        public float queueTimeTotal = 1f;
        public int quantity;
        public bool debugStructure;
        public bool disabled;
        public int observableId;
        public bool insufficientReqPenaltyActive;
        public int bufferDevelopedQuantity;
        public FakeModifierRecord power = new(100d);
        public FakeModifierRecord powerScaling = new(100d);
        public FakeModifierRecord speed = new(100d);
        public FakeModifierRecord passiveCostMod = new(100d);
        public FakeModifierRecord activeCostMod = new(100d);
        public FakeModifierRecord costScalingMod = new(100d);
        public FakeModifierRecord attributeRankEffectMod = new(100d);
        public FakeModifierRecord drainCostMod = new(100d);
        public FakeModifierRecord bonusLevels = new(0d);
        public FakeModifierRecord effectLevels = new(0d);
        public FakeModifierRecord buildSpeed = new(100d);
        public FakeModifierRecord echoBuildRating = new(0d);
        public FakeModifierRecord powerBuildRating = new(0d);
        public FakeCostList baseCost = new();
        public FakeModifierRef costPerQuantity = new();
        public FakePrerequisites prerequisitesPerLevel = new();

        public Guid GetGuid() => Identity;

        public int GetPurchaseLevel() => Level;

        public int GetQueuedQuantity() => Queued;

        public bool IsAvailable() => Available;
    }

    /// <summary>A structure's authored cost, shaped as the game shapes it: a list wrapper.</summary>
    private sealed class FakeCostList
    {
        public List<FakeCostEntry> costs = new();
    }

    /// <summary>
    /// One cost entry. The magnitude is the BigDouble field, not the serialized double beside it —
    /// that one is only what Unity writes to disk.
    /// </summary>
    private struct FakeCostEntry
    {
        public FakeReferencedEntity resource;
        public BigDouble valueBig;

        internal FakeCostEntry(Guid resourceId, double amount)
        {
            resource = new FakeReferencedEntity { Identity = resourceId };
            valueBig = new BigDouble(amount);
        }
    }

    /// <summary>A reference to a global modifier, which is what a structure holds rather than one.</summary>
    private sealed class FakeModifierRef
    {
        public FakeModifierVariable? variable;
    }

    private sealed class FakeUpgrade
    {
        public static readonly List<FakeUpgrade> All = new();

        public Guid Identity = Guid.NewGuid();
        public int Level;
        public int maxLevel;
        public bool Available = true;
        public int queuedLevels;
        public BigDouble buildTime;
        public double developmentTime = 5d;
        public int cachedCostLevel = -1;
        public FakeCostList resourceCost = new();
        public FakeModifierListRef resourceCostModPerLevel = new();
        public FakePrerequisites prerequisitesPerLevel = new();

        public Guid GetGuid() => Identity;

        public int GetPurchaseLevel() => Level;

        public bool IsAvailable() => Available;
    }

    /// <summary>
    /// What an upgrade holds instead of a modifier list: a reference that resolves either to one it
    /// names or to a shared standard. The resolution is behind GetValue() on both paths.
    /// </summary>
    private sealed class FakeModifierListRef
    {
        public FakeModifierList? variable;

        public FakeModifierList GetValue() => variable ?? Empty;

        private static readonly FakeModifierList Empty = new();
    }

    /// <summary>
    /// Two lists, not one. The exponents strengthen the modifiers before any of them touches a value,
    /// so flattening them together would change the arithmetic.
    /// </summary>
    private sealed class FakeModifierList
    {
        public List<FakeValueModifier> modifiers = new();
        public List<FakeValueModifier> exponents = new();
    }

    private sealed class FakeResearch
    {
        public static readonly List<FakeResearch> All = new();

        public Guid Identity = Guid.NewGuid();
        public int level;
        public int queuedLevels;
        public int researchStage;
        public int selfBonusLevels;
        public int maxLevel = 1;
        public double researchTime = 60d;
        public bool isDeveloping;
        public bool isActive;
        public bool flagged;
        public bool Available = true;
        public bool hiddenLevel;
        public int levelVisibilityRange = 2;
        public int requiredStagesCached;
        public BigDouble requiredTimeCached;
        public FakeModifierRecord requirementsAdjust = new(0d);
        public FakeModifierRecord bonusLevels = new(0d);
        public FakeModifierRecord baseLevels = new(0d);
        public FakeModifierRecord power = new(100d);
        public FakeModifierRecord maxLevelCap = new(0d);
        public FakeModifierRecord leewayPoints = new(0d);

        public Guid GetGuid() => Identity;

        public bool IsAvailable() => Available;
    }

    /// <summary>
    /// A global number variable. The two number registries hold the same shape but different
    /// instances, so they get a stand-in each — pointing both at one type would have the second walk
    /// re-offer entities the first already claimed.
    /// </summary>
    private sealed class FakeNumber
    {
        public static readonly List<FakeNumber> All = new();

        public Guid Identity = Guid.NewGuid();
        public FakeModifierRecord value = new(0d);
        public bool isPercentVariable;

        public Guid GetGuid() => Identity;
    }

    private sealed class FakeCount
    {
        public static readonly List<FakeCount> All = new();

        public Guid Identity = Guid.NewGuid();
        public FakeModifierRecord value = new(0d);
        public bool isPercentVariable;

        public Guid GetGuid() => Identity;
    }

    private sealed class FakeFlag
    {
        public static readonly List<FakeFlag> All = new();

        public Guid Identity = Guid.NewGuid();
        public bool value;

        public Guid GetGuid() => Identity;
        public bool initialValue;
        public bool isSaved;
        public int observerId;
    }

    /// <summary>
    /// The whole point of collecting the rate terms is that the ported chain can then run off the
    /// snapshot rather than calling <c>GetTrueRate()</c> on the Unity thread. That needs every
    /// argument, including the ones the game keeps private and the ones it only exposes as
    /// <c>HasActiveElements()</c>, which is an active-modifier count compared against zero.
    /// </summary>
    [Fact]
    public void AResourceCarriesEveryArgumentThePortedRateChainReads()
    {
        var mana = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            rate = new FakeModifierRecord(12d, activeCount: 2),
            rateSplash = new FakeModifierRecord(3d, activeCount: 1),
            rateMaxPercent = new FakeModifierRecord(4d),
            rateInterestPercent = new FakeModifierRecord(5d),
            rateMissingPercent = new FakeModifierRecord(6d),
            rateLifetimePercent = new FakeModifierRecord(7d),
            lossPercent = new FakeModifierRecord(8d),
            displayRate = new FakeModifierRecord(9d),
            calcRarityValue = new BigDouble(2.5d),
            baseLoss = 0.5d,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var row));

        var inputs = row.Reading.RateInputs;
        Assert.Equal(12d, inputs.Rate.ToDouble());
        Assert.Equal(3d, inputs.RateSplash.ToDouble());
        Assert.Equal(4d, inputs.RateMaxPercent.ToDouble());
        Assert.Equal(5d, inputs.RateInterestPercent.ToDouble());
        Assert.Equal(6d, inputs.RateMissingPercent.ToDouble());
        Assert.Equal(7d, inputs.RateLifetimePercent.ToDouble());
        Assert.Equal(8d, inputs.LossPercent.ToDouble());
        Assert.Equal(9d, inputs.DisplayRate.ToDouble());
        Assert.Equal(2.5d, inputs.CalcRarityValue.ToDouble());
        Assert.Equal(0.5d, inputs.BaseLoss);

        // HasActiveElements() is this count against zero, and the chain branches on it — a term with
        // no active modifiers drops out rather than contributing zero.
        Assert.Equal(2, inputs.RateModifiers);
        Assert.Equal(1, inputs.RateSplashModifiers);
        Assert.Equal(0, inputs.RateMaxPercentModifiers);
        Assert.Equal(0, inputs.RateLifetimePercentModifiers);
    }

    /// <summary>
    /// The five grouping types shared one row while each was read for two numbers. They are not one
    /// shape: a spell type alone carries twenty-two cached records, so the shared row could only stay
    /// shared by continuing to leave them out.
    /// </summary>
    [Fact]
    public void EachGroupingTypeCarriesItsOwnState()
    {
        var spell = new FakeSpellType { typeLevel = 6, typeXp = new BigDouble(1200d) };
        spell.power = new FakeModifierRecord(250d);
        spell.critEffectMod = new FakeModifierRecord(180d);
        FakeSpellType.All.Add(spell);

        var resourceType = new FakeResourceType { level = 3, freeLevels = 1, ignoreAudit = true };
        FakeResourceType.All.Add(resourceType);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.SpellTypes, spell.Identity, out var type));
        Assert.Equal(6, type.TypeLevel);
        Assert.Equal(1200d, type.TypeXp.ToDouble());
        Assert.Equal(250d, type.Power.ToDouble());
        Assert.Equal(180d, type.CritEffectMod.ToDouble());

        Assert.True(WorldLookup.TryFind(world.ResourceTypes, resourceType.Identity, out var kind));
        Assert.Equal(3, kind.Level);
        Assert.Equal(1, kind.FreeLevels);
        Assert.True(kind.IgnoreAudit);
    }

    /// <summary>
    /// An OrderedMultiplierRecord and a MergingModifierRecord are distributors rather than values:
    /// they hold modifiers and push them into the member records registered with <c>AddRecord</c>, so
    /// the effect arrives on the members and is already in the snapshot. What a fixed-size row can
    /// carry about the distributor itself is how many active modifiers it holds — the game's own
    /// <c>HasActiveElements()</c> — and that is what it carries.
    /// </summary>
    [Fact]
    public void AComposedRecordTravelsAsItsActiveModifierCountRatherThanAValue()
    {
        var type = new FakeAlchemyType();
        type.power = new FakeModifierRecord(0d, activeCount: 3);
        FakeAlchemyType.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, type.Identity, out var row));
        Assert.Equal(3, row.PowerModifiers);
        Assert.Equal(0, row.SpeedModifiers);
    }

    /// <summary>
    /// A harvest element owns a resource outright: it creates one with
    /// <c>ScriptableObject.CreateInstance</c>, never registers it, and marks it excluded from
    /// globals. The resource registry therefore cannot see it, and reading it through its owner is
    /// the only path — with the same member list every other resource is read with, so the two
    /// populations cannot drift apart.
    /// </summary>
    [Fact]
    public void AResourceAnElementOwnsIsReadThroughTheElement()
    {
        var element = new FakeHarvestElement();
        element.Resource.Quantity = new BigDouble(42d);
        element.Resource.maxQuantity = new FakeModifierRecord(100d);
        element.Resource.quality = new FakeModifierRecord(200d);
        FakeHarvestElement.All.Add(element);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(1, world.HarvestResources.Count);

        var row = world.HarvestResources[0];
        Assert.Equal(element.Identity, row.ElementId);
        Assert.Equal(42d, row.Resource.Reading.Quantity.ToDouble());

        // Derived exactly as any other resource row is — the quality scaling included.
        Assert.Equal(84d, row.Resource.TrueQuantity.ToDouble());
        Assert.True(row.Resource.IsCapped);

        // Its identity is the resource's own, not the element's: identity is claimed once across
        // every category, and the element has already claimed its own.
        Assert.NotEqual(element.Identity, row.EntityId);
        Assert.True(WorldLookup.TryFind(world.HarvestElements, element.Identity, out _));

        // And it stays out of the resource table, because the game keeps it out of the registry.
        Assert.Equal(0, world.Resources.Count);
    }

    /// <summary>
    /// A single-valued reference travels as the identity of what it points at, never as the object.
    /// The alchemy type's selected level is the case that motivated the rule: the game holds the
    /// choice in an <c>IntVariable</c> the global registry already collects, so building the row from
    /// the type's own fields found a reference, called it unpublishable, and dropped the edge — which
    /// left the snapshot unable to say which level was selected at all.
    /// </summary>
    [Fact]
    public void AReferenceToAnotherEntityTravelsAsThatEntitysIdentity()
    {
        var chosen = new FakeReferencedEntity();
        var rerolls = new FakeReferencedEntity();
        var choice = Guid.NewGuid();
        var levelling = new FakeReferencedEntity();

        var typeWithChoice = new FakeAlchemyType { selectedLevel = chosen };
        var typeWithout = new FakeAlchemyType();
        FakeAlchemyType.All.Add(typeWithChoice);
        FakeAlchemyType.All.Add(typeWithout);

        FakeDiscoveryTree.All.Add(new FakeDiscoveryTree
        {
            selectedChoiceId = new FakeGuidContainer(choice),
            overrideDiscoveryRerolls = rerolls,
        });

        var experience = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource
        {
            Identity = experience,
            appliedLevels = 14L,
            levelVariable = levelling,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithChoice.Identity, out var chose));
        Assert.Equal(chosen.Identity, chose.SelectedLevelId);

        // A type with no such choice reads as empty, which is the suite's own "no entity" and cannot
        // be mistaken for an edge — WorldTable refuses to admit a row keyed on it.
        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithout.Identity, out var didNot));
        Assert.Equal(Guid.Empty, didNot.SelectedLevelId);

        Assert.Equal(1, world.DiscoveryTrees.Count);
        var tree = world.DiscoveryTrees[0];
        Assert.Equal(choice, tree.SelectedChoiceId);
        Assert.Equal(rerolls.Identity, tree.OverrideRerollsId);
        Assert.Equal(Guid.Empty, tree.OverrideChoicesId);

        Assert.True(WorldLookup.TryFind(world.Resources, experience, out var resource));
        Assert.Equal(14L, resource.Reading.AppliedLevels);
        Assert.Equal(levelling.Identity, resource.Reading.LevelVariableId);
    }

    /// <summary>
    /// The tick's fixed timestep is read during collection, not during derivation:
    /// <c>Time.fixedDeltaTime</c> is main-thread-only and Build is the half that may run anywhere.
    /// </summary>
    [Fact]
    public void TheSnapshotCarriesTheTimestepReadAtCollectionTime()
    {
        var reads = 0;
        var collector = new GameWorldCollector(
            name => Defaults.TryGetValue(name, out var type) ? type : null,
            () =>
            {
                reads++;
                return 0.02d;
            });

        Assert.Equal(0, reads);

        collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, reads);
        Assert.Equal(0.02d, world.FixedDeltaTime);
    }

    /// <summary>
    /// A ritual's two senses of "active" are different questions and the row must answer both:
    /// <c>inBattle</c> is a run under way, while <c>ritualInstances.Count &gt; 0</c> — the game's own
    /// <c>HasActiveInstances()</c> and <c>IsDurationActive()</c> — is a finished run whose reward is
    /// still ticking. Reading the save record would have surfaced neither the count nor the tiers.
    /// </summary>
    [Fact]
    public void ARitualCarriesWhetherItIsUnlockedAndWhetherItsEffectsAreRunning()
    {
        var idle = Guid.NewGuid();
        var ticking = Guid.NewGuid();

        FakeRitual.All.Add(new FakeRitual
        {
            Identity = idle,
            discovered = true,
            durationRewardBlocks = { new object() },
        });
        FakeRitual.All.Add(new FakeRitual
        {
            Identity = ticking,
            discovered = true,
            inBattle = true,
            selectedLevel = 7,
            reachedLevel = 9,
            critLevel = 2,
            echoLevel = 1,
            chainLevel = 3,
            battleTotalWeight = new BigDouble(4200d),
            ritualInstances = new List<object> { new(), new() },
            durationRewardBlocks = { new object() },
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.Rituals, idle, out var quiet));
        Assert.True(quiet.Discovered);
        Assert.False(quiet.InBattle);

        // The list is null on this one, which is how the game leaves it before first use.
        Assert.Equal(0, quiet.ActiveInstances);
        Assert.Equal(1, quiet.DurationRewardBlocks);

        Assert.True(WorldLookup.TryFind(world.Rituals, ticking, out var running));
        Assert.True(running.InBattle);
        Assert.Equal(2, running.ActiveInstances);
        Assert.Equal(7, running.SelectedLevel);
        Assert.Equal(9, running.ReachedLevel);
        Assert.Equal(2, running.CritLevel);
        Assert.Equal(1, running.EchoLevel);
        Assert.Equal(3, running.ChainLevel);
        Assert.Equal(4200d, running.BattleTotalWeight.ToDouble());
    }

    /// <summary>
    /// Stock is the case that proves the point of D17: <c>ConsumableSO</c> keeps it in a private
    /// cached int, while the save record stores a list of per-level counts and rebuilds the total on
    /// load. Building the row from the save record left every item count out of the snapshot.
    /// </summary>
    [Fact]
    public void AConsumableCarriesItsStockEvenThoughTheSaveRecordDoesNot()
    {
        var potion = Guid.NewGuid();

        FakeConsumable.All.Add(new FakeConsumable
        {
            Identity = potion,
            visible = true,
            quantity = 5,
            queuedQuantity = 2,
            gainedSince = 3,
            maxCreatedLv = 4,
            prepSpeed = new ValueModifierRecord(new BigDouble(150d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Consumables, potion, out var row));
        Assert.True(row.Visible);
        Assert.Equal(5, row.Quantity);
        Assert.Equal(2, row.QueuedQuantity);
        Assert.Equal(3, row.GainedSince);
        Assert.Equal(4, row.MaxCreatedLevel);
        Assert.Equal(150d, row.Modifiers.PrepSpeed.ToDouble());
    }

    /// <summary>
    /// A structure's price is computed from what was collected, not asked of the game.
    /// </summary>
    /// <remarks>
    /// Every term is distinct and away from parity, so a chain that dropped one moves the answer
    /// instead of landing on a multiplier of one that was going to be one anyway. Derived by hand from
    /// the decompiled original, not from the port:
    /// <list type="number">
    /// <item>attribute: <c>100 × 200% = 200</c></item>
    /// <item>per-quantity <c>Raw 0.5</c>, scaled by cost scaling <c>300%</c> and two committed levels,
    /// is <c>Raw 3</c>, and Raw adds: <c>200 + 3 = 203</c></item>
    /// <item>the multiplier is <c>Max(120, 100 / (1 + 0.5 × 2)) × ((150 × 200%) as percent)</c>
    /// = <c>Max(120, 50) × 3 = 360</c>, whose percent is <c>3.6</c></item>
    /// <item><c>203 × 3.6 = 730.8</c>, which RoundToTwoSigsEarly leaves alone above 100</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void AStructuresPriceIsComputedFromWhatWasCollected()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeCount.All.Add(new FakeCount
        {
            Identity = KnownEntities.BulkDevelopment.Uuid,
            value = new FakeModifierRecord(3d),
        });
        FakePlayerGlobals.SetStructureCost(200d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0.5d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            Level = 1,
            Queued = 1,
            passiveCostMod = new FakeModifierRecord(120d),
            activeCostMod = new FakeModifierRecord(150d),
            costScalingMod = new FakeModifierRecord(300d),
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out var count));
        Assert.Equal(1, count);

        var row = world.PurchaseCosts[start];
        Assert.Equal(water, row.ResourceId);
        Assert.Equal(730.8d, row.Amount.ToDouble(), 6);
        Assert.Equal(3, row.ExactGroupedLevels);
        Assert.Equal(2208.6d, row.ExactGroupedAmount.ToDouble(), 6);
    }

    /// <summary>
    /// A structure whose per-quantity modifier does not resolve publishes no price at all.
    /// </summary>
    /// <remarks>
    /// The alternative — pricing it without the modifier — produces a number that is confidently too
    /// low, and Auto Buy cannot tell a cheap structure from a mispriced one. An absent price it can
    /// see and refuse to act on.
    /// </remarks>
    [Fact]
    public void AStructureWithNoResolvableScalingModifierIsNotPriced()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        // Reading the cost list still succeeded — the shortfall is in what could be derived from it,
        // which is why this is not a collection failure.
        Assert.True(report.IsComplete, report.Describe());
        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// A price paid in several resources is several rows, and the lookup returns all of them.
    /// </summary>
    /// <remarks>
    /// The range is the whole point of the table's shape: a binary search that landed anywhere inside
    /// an entity's rows and walked forward would report a partial price, and a consumer checking
    /// affordability against a partial price buys things it cannot pay for.
    /// </remarks>
    [Fact]
    public void AMultiResourcePriceIsReturnedWhole()
    {
        var water = Guid.NewGuid();
        var mana = Guid.NewGuid();
        var stone = Guid.NewGuid();
        var perQuantity = Guid.NewGuid();

        foreach (var resource in new[] { water, mana, stone })
            FakeResource.All.Add(new FakeResource { Identity = resource, Quantity = 500d });

        var modifier = new FakeModifierVariable
        {
            Identity = perQuantity,
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);

        // Two structures, so the search has to find the right one's first row rather than the table's.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = first,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d), (mana, 200d), (stone, 300d)),
        });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = second,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, first, out var start, out var count));
        Assert.Equal(3, count);

        var total = 0d;
        for (var index = start; index < start + count; index++)
        {
            Assert.Equal(first, world.PurchaseCosts[index].EntityId);
            total += world.PurchaseCosts[index].Amount.ToDouble();
        }

        Assert.Equal(600d, total, 6);

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, second, out _, out var otherCount));
        Assert.Equal(1, otherCount);
    }

    /// <summary>
    /// A resource whose attribute-cost modifier has never been calculated takes the price with it.
    /// </summary>
    /// <remarks>
    /// The game authors that modifier at 100 and reaches it through an accessor that recalculates on
    /// demand; collection reads the cached field and does not. So a zero means "not yet calculated",
    /// and pricing on it would multiply the whole entity to nothing — the one error direction that
    /// makes Auto Buy buy something it cannot afford. See W5.
    /// </remarks>
    [Fact]
    public void AnUncalculatedAttributeModifierWithholdsThePriceRatherThanZeroingIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(0d),
        });

        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The attribute-cost modifier is the game's quotient, so a quality bonus makes a structure
    /// cheaper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetAttributeCostMod()</c> is <c>attributeCostMod / Pow(quality.AsPercent(), bonus)</c>.
    /// Here that is <c>200 / Pow(4, 2) = 12.5</c>, whose percent is <c>0.125</c>, against the
    /// <c>2</c> the numerator alone would have given.
    /// </para>
    /// <para>
    /// The rest is the fixture from the test above, and the discount does <em>not</em> just divide
    /// its answer: it lands on the base cost before the per-quantity modifier adds to it. So
    /// <c>100 × 0.125 = 12.5</c>, then Raw adds its 3 for <c>15.5</c>, then the multiplier of 3.6
    /// gives <c>55.8</c> — and that one is under 100, where RoundToTwoSigsEarly does its only work,
    /// snapping it to <c>56</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void AQualityBonusDiscountsAStructuresPriceTheWayTheGameDiscountsIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetStructureCost(200d);
        FakePlayerGlobals.SetAttributeQualityBonus(2d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(400d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(56d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// With no bonus earned, the quality discount is one and the price is the undiscounted one.
    /// </summary>
    /// <remarks>
    /// The exponent, not the quality, is what switches the discount off: <c>Pow(4, 0) = 1</c> whatever
    /// the quality is. This pins the neutral case on the term that actually carries it, so a reading
    /// that dropped the exponent and divided by the quality itself would fail here rather than only
    /// on a developed save.
    /// </remarks>
    [Fact]
    public void WithoutTheQualityBonusAStructurePricesAsThoughTheDiscountWereAbsent()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetStructureCost(200d);
        FakePlayerGlobals.SetAttributeQualityBonus(0d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(400d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(730.8d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A resource whose quality has never been calculated takes the price with it, like its
    /// attribute-cost modifier does.
    /// </summary>
    /// <remarks>
    /// Quality is the base of the power the discount divides by, so a zero divides the price by zero
    /// and publishes an infinity — the same W5 refusal as an uncalculated attribute modifier, from the
    /// other end of the quotient. An infinite price is not the safe direction either: it is a number
    /// no consumer can compare against and every downstream sum turns into a NaN.
    /// </remarks>
    [Fact]
    public void AnUncalculatedQualityWithholdsThePriceRatherThanSendingItToInfinity()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetAttributeQualityBonus(2d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(0d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The overpricing this fixed has a signature: the error is one factor per resource, the same for
    /// every structure priced in that resource, whatever their levels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Live spin 6 published every one of the 342 structure cost rows too high and not one of the 402
    /// upgrade rows, because only the structure chain calls <c>AdjustAsAttribute</c>. The diagnosis
    /// that stuck first read the gap as <c>costPerQuantity^committed</c> — but the two spins that
    /// measured it disagree with that and agree with this: on the same resource, Rapid Gathering and
    /// Transmute Power were over by <c>1.8107e133</c> and <c>1.8173e133</c> despite differing by 208
    /// committed levels, and a level-driven fault would have separated them by <c>1.25^208</c>.
    /// </para>
    /// <para>
    /// So this asserts the shape rather than a number: two structures at different levels, one
    /// resource, one bonus. Without the divisor both prices are wrong; with it both are right. What
    /// makes it a regression test is the ratio — it is identical across the two, which is the
    /// fingerprint that named the term.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheQualityDiscountIsOneFactorPerResourceRatherThanOnePerLevel()
    {
        var water = Guid.NewGuid();
        var shallow = Guid.NewGuid();
        var deep = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(1000d),
            attributeCostMod = new FakeModifierRecord(100d),
        });

        // Multiplicative, so the levels genuinely price apart — 1.25²⁰⁸ between them — while still
        // cancelling exactly out of each structure's own before-and-after ratio.
        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.MultiStacking, 1.25d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);

        FakeStructure Structure(Guid identity, int level) => new()
        {
            Identity = identity,
            Level = level,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 1e12d)),
        };

        FakeStructure.All.Add(Structure(shallow, level: 1));
        FakeStructure.All.Add(Structure(deep, level: 209));

        var collector = Collector();

        FakePlayerGlobals.SetAttributeQualityBonus(0d);
        collector.Collect();
        var undiscounted = collector.Build();
        var shallowBefore = PriceOf(undiscounted, shallow);
        var deepBefore = PriceOf(undiscounted, deep);

        FakePlayerGlobals.SetAttributeQualityBonus(3d);
        collector.Collect();
        var discounted = collector.Build();
        var shallowAfter = PriceOf(discounted, shallow);
        var deepAfter = PriceOf(discounted, deep);

        // The two are 1.25²⁰⁸ apart, so a fault that scaled with level could not show one ratio.
        Assert.NotEqual(shallowBefore, deepBefore);

        Assert.Equal(shallowBefore / shallowAfter, deepBefore / deepAfter, 6);

        // Pow(1000%, 3) = 10³, the one factor the resource contributes however deep the structure is.
        Assert.Equal(1000d, shallowBefore / shallowAfter, 6);
    }

    /// <summary>The published price of an entity's single row.</summary>
    private static double PriceOf(GameWorldState world, Guid entityId)
    {
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, entityId, out var start, out _));
        return world.PurchaseCosts[start].Amount.ToDouble();
    }

    /// <summary>The per-quantity modifier the priced-structure fixtures share: <c>Raw 0.5</c>.</summary>
    private static FakeModifierVariable PerQuantity() =>
        new()
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0.5d, order: 0),
        };

    /// <summary>
    /// The fixture from <see cref="AStructuresPriceIsComputedFromWhatWasCollected"/>, whose hand-derived
    /// price is 730.8 before any quality discount.
    /// </summary>
    private static FakeStructure PricedCauldron(Guid identity, Guid resource, FakeModifierVariable modifier) =>
        new()
        {
            Identity = identity,
            Level = 1,
            Queued = 1,
            passiveCostMod = new FakeModifierRecord(120d),
            activeCostMod = new FakeModifierRecord(150d),
            costScalingMod = new FakeModifierRecord(300d),
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((resource, 100d)),
        };

    /// <summary>
    /// An upgrade prices off the list it grows by, with the exponents kept apart from the modifiers.
    /// </summary>
    /// <remarks>
    /// Every term is deliberately off parity, because a fixture built from defaults tests almost
    /// nothing about a multiplicative chain — the defaults are the identity and every wire crosses
    /// unobserved. Derived by hand: three committed levels price level four, so both lists scale by
    /// three; the stacking modifier becomes 2³ = 8 and the raw exponent becomes 3; the exponent
    /// strengthens the modifier to 8⁽¹⁺³⁾ = 4096 before it touches the cost; 50 × 4096 = 204800,
    /// snapped to two significant digits at 200000. A reader that dropped the exponent list would
    /// publish 400 instead.
    /// </remarks>
    [Fact]
    public void AnUpgradePricesOffTheModifierListItGrowsBy()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 2,
            queuedLevels = 1,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(
                new[] { new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0) },
                new[] { new FakeValueModifier(FakeModifierKind.Raw, 1d, order: 0) }),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(water, world.PurchaseCosts[start].ResourceId);
        Assert.Equal(200000d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// Two per-level modifiers at different orders are applied in sequence, not merged.
    /// </summary>
    /// <remarks>
    /// The order field is the one part of a modifier that is silently substitutable: a reader that
    /// never bound it, or bound it to a constant, still produces a plausible number. Here it changes
    /// the answer. Three committed levels scale the stacking modifier to 2³ = 8 and the raw one to 30;
    /// at separate orders that is 50 × 8 + 30 = 430, and at the same order the raw one merges in first
    /// for (50 + 30) × 8 = 640.
    /// </remarks>
    [Fact]
    public void PerLevelModifiersAtDifferentOrdersAreAppliedInSequence()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 3,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(new[]
            {
                new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0),
                new FakeValueModifier(FakeModifierKind.Raw, 10d, order: 1),
            }),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(430d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A finite upgrade keeps quoting the last level it can buy rather than one past the ceiling.
    /// </summary>
    /// <remarks>
    /// The cap is the game's, and it is easy to leave out because nothing else about an upgrade
    /// mentions it: <c>min(level + queued, maxLevel - 1)</c>. Both upgrades here sit at five committed
    /// levels and differ only in whether a ceiling applies — the bounded one prices level three at
    /// 50 × 2² and the unbounded one prices level six at 50 × 2⁵, so an implementation that ignored
    /// the cap would quote the maxed-out upgrade eight times too much.
    /// </remarks>
    [Fact]
    public void AFiniteUpgradeStopsRepricingOnceItsCeilingIsReached()
    {
        var water = Guid.NewGuid();
        var bounded = Guid.NewGuid();
        var unbounded = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        foreach (var (identity, maxLevel) in new[] { (bounded, 3), (unbounded, 0) })
        {
            FakeUpgrade.All.Add(new FakeUpgrade
            {
                Identity = identity,
                Level = 5,
                maxLevel = maxLevel,
                resourceCost = CostOf((water, 50d)),
                resourceCostModPerLevel = LevelModifiers(
                    new[] { new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0) }),
            });
        }

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, bounded, out var cappedRow, out _));
        Assert.Equal(200d, world.PurchaseCosts[cappedRow].Amount.ToDouble(), 6);

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, unbounded, out var openRow, out _));
        Assert.Equal(1600d, world.PurchaseCosts[openRow].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade with nothing to grow by still publishes a price, rounded.
    /// </summary>
    /// <remarks>
    /// A flat-cost upgrade is ordinary in this game, not a degraded reading: <c>SetToLevel</c> simply
    /// skips its scaling branch. The rounding still runs, which is what 137 landing at 140 shows —
    /// treating an empty list as "could not price this" would withhold every flat upgrade, and
    /// returning the authored value untouched would disagree with the game by three.
    /// </remarks>
    [Fact]
    public void AnUpgradeWithNothingToGrowByStillPublishesARoundedPrice()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 4,
            resourceCost = CostOf((water, 137d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(140d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade prices even when the resource's attribute-cost modifier reads zero.
    /// </summary>
    /// <remarks>
    /// That modifier withholds a <em>structure's</em> price, because the structure chain multiplies by
    /// it and a zero would make the structure free. The upgrade chain never touches it, so making it a
    /// shared precondition would withhold upgrade prices for a reading that says nothing about them.
    /// </remarks>
    [Fact]
    public void AnUpgradeDoesNotWaitOnTheAttributeModifierAStructureNeeds()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(0d),
        });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            resourceCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(400d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A plot node's quantities are summed out of its phase instances, not asked for.
    /// </summary>
    /// <remarks>
    /// The game's own <c>GetQuantity()</c> and <c>GetTotalQuantity()</c> are closed to collection:
    /// both reach the instances through <c>GetPhaseInstance()</c>, which lazily builds a cache and
    /// creates a missing instance on the way past — a write, from a pass whose contract is that it
    /// does not write. Three idle, four growing and five resting is twelve in total and three idle,
    /// and with two claimed by a main-phase action and one by an any-phase one the any-phase claim is
    /// absorbed by the nine busy, leaving 3 - 2 - 0 = 1. The three counts are deliberately distinct:
    /// equal ones let a reader that ignores an instance's phase agree with one that does not.
    /// </remarks>
    [Fact]
    public void APlotNodesQuantitiesAreSummedFromItsPhaseInstances()
    {
        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            actionQuantityUsageMain = new FakeModifierRecord(2d),
            actionQuantityUsageAny = new FakeModifierRecord(1d),
        }
            .With(FakePlotPhase.Idle, 3)
            .With(FakePlotPhase.Growing, 4)
            .With(FakePlotPhase.Resting, 5));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.IdleQuantity);
        Assert.Equal(12, row.Reading.TotalQuantity);
        Assert.Equal(1, row.RemainingQuantity);
    }

    /// <summary>
    /// A phase instance the node does not author is not part of its total.
    /// </summary>
    /// <remarks>
    /// The game totals by walking <c>phaseInfos</c> — the authored phases — and asking for each
    /// one's instance, so an instance for a phase that is not authored contributes nothing. Summing
    /// the instance list instead would be the obvious implementation and would count it, putting the
    /// suite's total quietly out of step with every calculation the game bases on its own.
    /// </remarks>
    [Fact]
    public void AnInstanceForAPhaseTheNodeDoesNotAuthorIsNotCounted()
    {
        var node = Guid.NewGuid();
        var plot = new FakePlotNode { Identity = node }.With(FakePlotPhase.Idle, 3);
        plot.phaseInstances.Add(new FakePhaseInstance
        {
            phase = FakePlotPhase.Resting,
            timers = new FakeTimerList { q = 5 },
        });
        FakePlotNode.All.Add(plot);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.TotalQuantity);
    }

    /// <summary>
    /// A pair exists for every action a plot offers and every action it is running, and the two are
    /// the same row when they are the same action.
    /// </summary>
    /// <remarks>
    /// The pair is what the readiness question is asked about, and it is asked of plots that offer an
    /// action they are not running and of plots running one they no longer offer. A table keyed on
    /// either side alone loses one of those two. Both sides are counted rather than flagged, so a
    /// consumer that needs the pair to be unambiguous can see that it is not.
    /// </remarks>
    [Fact]
    public void EachPlotAndActionPairIsPublishedOnce()
    {
        var offered = new FakePlotNodeAction();
        var alsoOffered = new FakePlotNodeAction();
        var retired = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { offered, alsoOffered, retired });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 7)
            .Offering(offered)
            .Offering(alsoOffered)
            .Offering(offered)
            .Running(offered)
            .Running(retired));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(3, world.PlotActions.Count);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, offered.Identity, out var both));
        Assert.Equal(2, both.Reading.OfferedCount);
        Assert.Equal(1, both.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, alsoOffered.Identity, out var idle));
        Assert.Equal(1, idle.Reading.OfferedCount);
        Assert.Equal(0, idle.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, retired.Identity, out var orphan));
        Assert.Equal(0, orphan.Reading.OfferedCount);
        Assert.Equal(1, orphan.Reading.InstanceCount);
    }

    /// <summary>
    /// An instance whose reference has not been resolved yet still names its action.
    /// </summary>
    /// <remarks>
    /// <c>IdObjectRef</c> holds a serialized string and a guid it memoises the parse into, and the
    /// guid is empty until something asks. Straight off a save load nothing has, so a reader that
    /// trusted the memoised field alone would see a plot with no running actions at exactly the
    /// moment a consumer most wants to know what is already running.
    /// </remarks>
    [Fact]
    public void AnInstanceIsFoundThroughItsSerializedReferenceWhenTheGuidIsNotMemoisedYet()
    {
        var action = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 4)
            .RunningUnresolved(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(1, row.Reading.InstanceCount);
    }

    /// <summary>
    /// What one run costs is the authored cost divided by the plot's size, and how many runs fit
    /// depends on which of the plot's two remainders the action draws on.
    /// </summary>
    /// <remarks>
    /// Seven idle and five resting is twelve; one claimed by a main-phase action leaves six idle and
    /// eleven in total. A cost of seven against a size of 200% floors to three — rounding would give
    /// four — so the idle-paying action fits twice and the any-state one three times. Every number
    /// here is chosen so that swapping either remainder, dropping the size division, or rounding
    /// instead of flooring gives a different answer.
    /// </remarks>
    [Fact]
    public void AnActionsCostScalesByPlotSizeAndItsRunsByTheRemainderItDrawsOn()
    {
        var scaled = new FakePlotNodeAction { elementCost = 7, useSizeModForCost = true };
        var anyState = new FakePlotNodeAction { elementCost = 3, useAnyStateForCost = true };
        var idlePaying = new FakePlotNodeAction { elementCost = 3 };
        FakePlotNodeAction.All.AddRange(new[] { scaled, anyState, idlePaying });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            sizeMod = new FakeModifierRecord(200d),
            actionQuantityUsageMain = new FakeModifierRecord(1d),
        }
            .With(FakePlotPhase.Idle, 7)
            .With(FakePlotPhase.Resting, 5)
            .Offering(scaled)
            .Offering(anyState)
            .Offering(idlePaying));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, scaled.Identity, out var sized));
        Assert.True(sized.ElementCostKnown);
        Assert.Equal(3, sized.ElementCost);
        Assert.True(sized.HasEnoughForOneInstance);
        Assert.Equal(2, sized.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, anyState.Identity, out var any));
        Assert.Equal(3, any.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, idlePaying.Identity, out var idle));
        Assert.Equal(2, idle.MaximumRemainingInstances);
    }

    /// <summary>
    /// An action that takes its size from other nodes publishes no cost at all.
    /// </summary>
    /// <remarks>
    /// The game multiplies such a cost by a product over those nodes' next-size percentages, which
    /// this suite has not ported. Publishing the unscaled cost instead would be too cheap in the one
    /// direction that matters: a consumer would start a run the plot cannot pay for.
    /// </remarks>
    [Fact]
    public void AnActionThatTakesItsSizeFromOtherNodesPublishesNoCost()
    {
        var action = new FakePlotNodeAction { elementCost = 4, useSizeModForCost = true };
        action.sizeModNodes.Add(new FakePlotNode());
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node, sizeMod = new FakeModifierRecord(100d) }
            .With(FakePlotPhase.Idle, 9)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.False(row.ElementCostKnown);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Whether the game has confirmed an action's prerequisites is read from the latch it already set.
    /// </summary>
    /// <remarks>
    /// <c>Prerequisites.Container.Check()</c> stamps a game id and, on success, sets and leaves
    /// <c>available</c>. Collection reads the field. What that proves is one-directional: a set latch
    /// is a verdict the game reached, and an unset one is either a prerequisite the game found unmet
    /// or one it has not looked at, with nothing readable telling the two apart. The reading is named
    /// for that difference, and refuses to act either way.
    /// </remarks>
    [Fact]
    public void AnActionsPrerequisitesAreReadFromTheLatchRatherThanAsked()
    {
        var confirmed = new FakePlotNodeAction();
        confirmed.prerequisites.available = true;
        var unconfirmed = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { confirmed, unconfirmed });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 2)
            .Offering(confirmed)
            .Offering(unconfirmed));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, confirmed.Identity, out var allowed));
        Assert.True(allowed.Reading.PrerequisitesConfirmed);

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, unconfirmed.Identity, out var blocked));
        Assert.False(blocked.Reading.PrerequisitesConfirmed);
    }

    /// <summary>
    /// A plot with nothing left to give still publishes its pairs, with nothing available on them.
    /// </summary>
    /// <remarks>
    /// The game's own remainder is allowed to go negative — its two usage terms are subtracted
    /// asymmetrically — and dividing a negative remainder by a cost is where a faithful port turns
    /// into a consumer being told it may run an action a negative number of times.
    /// </remarks>
    [Fact]
    public void AnOverclaimedPlotOffersNoRunsRatherThanNegativeOnes()
    {
        var action = new FakePlotNodeAction { elementCost = 2 };
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            actionQuantityUsageMain = new FakeModifierRecord(5d),
        }
            .With(FakePlotPhase.Idle, 2)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(2, row.ElementCost);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Each of a plot's action instances is a row of its own, so "which one" is answerable.
    /// </summary>
    /// <remarks>
    /// The pair's count says a plot is running an action; it cannot say which of two instances, how
    /// much either is running, or whether either is under way. That is the question the action
    /// boundary walks the plot's list to answer on every submission, and these rows are that walk
    /// taken once. An instance whose reference resolves to nothing keeps a row keyed on no action:
    /// the plot really is holding it, and a table that dropped it would report the plot as holding
    /// one fewer.
    /// </remarks>
    [Fact]
    public void EachOfAPlotsActionInstancesIsARowOfItsOwn()
    {
        var collecting = new FakePlotNodeAction();
        var retired = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { collecting, retired });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 9)
            .Offering(collecting)
            .Running(collecting, quantity: 3, engaged: true)
            .Running(collecting, quantity: 0)
            .Running(retired, quantity: 2)
            .RunningSomethingUnknown());

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, collecting.Identity, out var pair));
        Assert.Equal(2, pair.Reading.InstanceCount);

        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, collecting.Identity, out var start, out var count));
        Assert.Equal(2, count);

        var running = world.PlotActionInstances[start];
        Assert.Equal(0, running.Ordinal);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);
        Assert.False(running.Empty);
        Assert.True(running.ReferenceResolved);

        var idle = world.PlotActionInstances[start + 1];
        Assert.Equal(1, idle.Ordinal);
        Assert.Equal(0, idle.Quantity);
        Assert.False(idle.Engaged);
        Assert.True(idle.Empty);

        // An instance of an action the plot no longer offers is still the plot's instance.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, retired.Identity, out var orphanStart, out var orphanCount));
        Assert.Equal(1, orphanCount);
        Assert.Equal(2, world.PlotActionInstances[orphanStart].Ordinal);

        // The unresolvable one is keyed on no action, which is the only honest key for it.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, Guid.Empty, out var unknownStart, out var unknownCount));
        Assert.Equal(1, unknownCount);
        Assert.False(world.PlotActionInstances[unknownStart].ReferenceResolved);
        Assert.Equal(3, world.PlotActionInstances[unknownStart].Ordinal);

        // And it is not a pair: a pair with no action on one side is not one.
        Assert.False(WorldPlotActionLookup.TryFind(world.PlotActions, node, Guid.Empty, out _));
    }

    /// <summary>
    /// One plot's instances stay that plot's, and a second pass publishes each of them once.
    /// </summary>
    /// <remarks>
    /// The instance buffer is reused like every other, and it has no identity check behind it to
    /// catch a missed reset — a plot's instances would simply appear twice, which reads exactly like
    /// a plot running the action twice.
    /// </remarks>
    [Fact]
    public void CollectingTwicePublishesEachInstanceOnce()
    {
        var action = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(action);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = first }
            .With(FakePlotPhase.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 1));
        FakePlotNode.All.Add(new FakePlotNode { Identity = second }
            .With(FakePlotPhase.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 5));

        var collector = Collector();
        collector.Collect();
        collector.Collect();
        var world = collector.Build();

        Assert.Equal(2, world.PlotActionInstances.Count);
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, second, action.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(5, world.PlotActionInstances[start].Quantity);
    }

    /// <summary>
    /// The plot-action queue publishes its occupancy and what sits in each of its slots.
    /// </summary>
    /// <remarks>
    /// This is the reading Auto Harvest takes one slot at a time at its action boundary, taken once
    /// for every consumer instead. The empty slot still gets a row: "position two is free" and
    /// "position two was not read" are different facts, and a table that only held occupants could
    /// not tell them apart.
    /// </remarks>
    [Fact]
    public void ThePlotActionQueueCarriesItsOccupancyAndWhatSitsInEachSlot()
    {
        var collecting = new FakePlotNodeAction();
        var growing = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { collecting, growing });

        var orchard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        var hoard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        FakePlotNode.All.AddRange(new[] { orchard, hoard });

        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 3, engaged = true, plot = orchard, action = collecting });
        queue.value.Add(new FakeQueueSlot());
        queue.value.Add(new FakeQueueSlot { quantity = 1, plot = hoard, action = growing });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(3, row.SlotCount);
        Assert.Equal(2, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.HasEmptySlot);
        Assert.True(row.Consistent);

        // Its capacity is the length of its own list, so there is no variable to point at.
        Assert.Equal(Guid.Empty, row.MaxQueuedItemsId);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, queue.Identity, out var start, out var count));
        Assert.Equal(3, count);

        var running = world.ActionQueueSlots[start];
        Assert.Equal(0, running.Index);
        Assert.False(running.Empty);
        Assert.Equal(orchard.Identity, running.PlotNodeId);
        Assert.Equal(collecting.Identity, running.PlotNodeActionId);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);

        var free = world.ActionQueueSlots[start + 1];
        Assert.True(free.Empty);
        Assert.Equal(Guid.Empty, free.PlotNodeId);
        Assert.Equal(0, free.Quantity);
        Assert.False(free.Engaged);

        var queued = world.ActionQueueSlots[start + 2];
        Assert.Equal(2, queued.Index);
        Assert.Equal(hoard.Identity, queued.PlotNodeId);
        Assert.Equal(growing.Identity, queued.PlotNodeActionId);
        Assert.False(queued.Engaged);

        Assert.False(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, Guid.NewGuid(), out _, out _));
    }

    /// <summary>
    /// The equipped loadout publishes one row per readable position, keyed by the game's own index.
    /// </summary>
    /// <remarks>
    /// The index is the whole point. It is what a cast is addressed by, so it has to count the holes
    /// the player leaves in the loadout — a table that renumbered its rows densely would still look
    /// right in every assertion about occupancy and would fire the wrong spell.
    /// </remarks>
    [Fact]
    public void TheEquippedLoadoutIsPublishedByThePositionACastIsAddressedBy()
    {
        var fireball = new FakeSpellRecipe { discovered = true };
        FakeSpellRecipe.All.Add(fireball);

        var water = Guid.NewGuid();
        var mana = Guid.NewGuid();

        var equipped = new FakeSpell
        {
            spellReference = fireball,
            chargeable = true,
            castReady = true,
            currentCharges = 2,
            maximumCharges = 3,
            cooldownRemaining = new BigDouble(4d, 0),
            cost = new FakeSpellCostList().With(water, 50d),
            drainCost = new FakeSpellCostList().With(mana, 7d),
        };

        var loadout = new FakeSpellLoadout();
        loadout.value.Add(equipped);
        loadout.value.Add(null);
        loadout.value.Add(new FakeSpell { empty = true });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        // The hole publishes nothing at all, so two rows for three positions.
        Assert.Equal(2, world.SpellSlots.Count);

        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 0, out var first));
        Assert.True(first.Occupied);
        Assert.Equal(fireball.Identity, first.SpellRecipeId);
        Assert.True(first.Chargeable);
        Assert.True(first.CastReady);
        Assert.Equal(2, first.CurrentCharges);
        Assert.Equal(3, first.MaximumCharges);
        Assert.Equal(4d, first.CooldownRemaining.ToDouble());

        // The empty position keeps its index rather than sliding down into the hole's place.
        Assert.False(WorldSpellSlotLookup.TryFind(world.SpellSlots, 1, out _));
        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 2, out var third));
        Assert.False(third.Occupied);
        Assert.Equal(Guid.Empty, third.SpellRecipeId);

        // Both prices are published against the same position, and they do not run together.
        Assert.True(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 0, WorldSpellCostKind.Immediate, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(water, world.SpellCosts[start].ResourceId);
        Assert.Equal(50d, world.SpellCosts[start].Amount.ToDouble());

        Assert.True(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 0, WorldSpellCostKind.Drain, out var drainStart, out var drainCount));
        Assert.Equal(1, drainCount);
        Assert.Equal(mana, world.SpellCosts[drainStart].ResourceId);
        Assert.Equal(7d, world.SpellCosts[drainStart].Amount.ToDouble());

        // An empty position is priced at nothing rather than at whatever the last one cost.
        Assert.False(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 2, WorldSpellCostKind.Immediate, out _, out _));
    }

    /// <summary>
    /// Every per-slot state the game distinguishes is published, one field per question.
    /// </summary>
    /// <remarks>
    /// These are the terms a planner skips on, and the game answers them separately because they mean
    /// different things — a channelling spell occupies the caster, a toggled one is already up, and an
    /// attuning one is neither. Folding them into one "busy" flag would lose the reason, and the
    /// reason is what a refusal has to be explained by.
    /// </remarks>
    [Fact]
    public void EachSlotCarriesTheGamesOwnAnswerForEveryStateItDistinguishes()
    {
        var loadout = new FakeSpellLoadout();
        loadout.value.Add(new FakeSpell
        {
            spellReference = null,
            casting = true,
            readyingCast = true,
            attuning = true,
            channeled = true,
            toggled = true,
            chargeable = true,
            castReady = false,
            chargeAvailable = false,
            resourcesCovered = false,
        });
        loadout.value.Add(new FakeSpell());
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 0, out var busy));
        Assert.True(busy.Casting);
        Assert.True(busy.ReadyingCast);
        Assert.True(busy.Attuning);
        Assert.True(busy.Channeled);
        Assert.True(busy.Toggled);
        Assert.True(busy.Chargeable);
        Assert.False(busy.CastReady);
        Assert.False(busy.ChargeAvailable);
        Assert.False(busy.ResourcesCovered);

        // An occupant with no recipe behind it still publishes a row: the slot is filled, and a
        // consumer that cannot name what is in it should see that rather than see nothing.
        Assert.True(busy.Occupied);
        Assert.Equal(Guid.Empty, busy.SpellRecipeId);

        // The neighbouring slot is the negative of all of it, from the same pass.
        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 1, out var idle));
        Assert.False(idle.Casting);
        Assert.False(idle.ReadyingCast);
        Assert.False(idle.Attuning);
        Assert.False(idle.Channeled);
        Assert.False(idle.Toggled);
        Assert.True(idle.CastReady);
    }

    /// <summary>
    /// A queue whose own occupancy disagrees with the slots publishes the disagreement.
    /// </summary>
    /// <remarks>
    /// The action boundary treats this as an invalid reading and refuses it, and it is right to: it
    /// is about to act. Collection is not acting, and a consumer that cannot see the disagreement
    /// would act on whichever half it happened to read — so the contradiction travels as a fact
    /// rather than as an absence.
    /// </remarks>
    [Fact]
    public void AQueueWhoseOccupancyContradictsItsSlotsSaysSo()
    {
        var queue = new FakeActionQueue { ReportedUsedSpots = 2 };
        queue.value.Add(new FakeQueueSlot { quantity = 1 });
        queue.value.Add(new FakeQueueSlot());
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(2, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.False(row.Consistent);
    }

    /// <summary>
    /// An entry the reader's accessors were not bound against costs the whole queue reading.
    /// </summary>
    /// <remarks>
    /// Ported from the action boundary, which makes exactly this check before it reads a slot. Every
    /// accessor here is compiled against the instance type; an entry of another type is not a slot
    /// this reader can read, and half a queue is worse than a queue reported as unread.
    /// </remarks>
    [Fact]
    public void AQueueEntryOfAnotherTypeCostsTheWholeReading()
    {
        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 1 });
        queue.value.Add(new FakeForeignQueueSlot { quantity = 1 });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        var queues = report.For("action queues");
        Assert.Equal(0, queues.Sampled);
        Assert.Equal(1, queues.Skipped);
        Assert.Contains("not a plot-action instance", queues.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.IsComplete);

        // Nothing partial reached the snapshot — not the queue, and not the slot that did read.
        Assert.Equal(0, world.ActionQueues.Count);
        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// A slot naming a pair no table holds still publishes the pair it names.
    /// </summary>
    /// <remarks>
    /// The queue is read before anything is known about what the other categories collected, and it
    /// stays that way: a slot is what the game says is in it. Dropping a slot whose plot or action
    /// is not in the snapshot would make the queue look emptier than it is, which is the one
    /// direction that makes a consumer plan another action into a queue that is full.
    /// </remarks>
    [Fact]
    public void ASlotNamingAPairNoTableHoldsIsStillPublished()
    {
        var stranger = new FakePlotNodeAction();
        var elsewhere = new FakePlotNode { Identity = Guid.NewGuid() };

        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 1, plot = elsewhere, action = stranger });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(0, world.PlotNodes.Count);
        Assert.Equal(0, world.PlotActions.Count);

        var slot = world.ActionQueueSlots[0];
        Assert.Equal(elsewhere.Identity, slot.PlotNodeId);
        Assert.Equal(stranger.Identity, slot.PlotNodeActionId);
        Assert.False(slot.Empty);
    }

    /// <summary>
    /// A hole in the list is a slot nothing is running in, which is what an empty slot is.
    /// </summary>
    [Fact]
    public void AHoleInTheQueueIsReadAsAnEmptySlot()
    {
        var queue = new FakeActionQueue();
        queue.value.Add(null);
        queue.value.Add(new FakeQueueSlot { quantity = 2 });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(1, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.Consistent);
        Assert.True(world.ActionQueueSlots[0].Empty);
    }

    /// <summary>
    /// The attribute queue publishes occupancy and the variable its maximum lives in, and nothing
    /// per-slot.
    /// </summary>
    /// <remarks>
    /// Its entries are actionables of every kind the game queues; what one of them is doing is a
    /// question nobody has asked the game, and inventing an answer would be describing a shape this
    /// suite has not read. The maximum travels as an edge because the game already publishes the
    /// number: it is an <c>IntVariable</c>, and that registry is collected whole.
    /// </remarks>
    [Fact]
    public void TheAttributeQueueCarriesOccupancyAndTheVariableItsMaximumLivesIn()
    {
        var maximum = Guid.NewGuid();
        FakeCount.All.Add(new FakeCount { Identity = maximum, value = new FakeModifierRecord(6d) });

        var queue = new FakeAttributeQueue
        {
            maxQueuedItems = new FakeQueueCapacity { Identity = maximum, Maximum = 6 },
            value = { new object(), new object() },
        };
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveActionables.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(2, row.UsedSlots);
        Assert.True(row.HasEmptySlot);
        Assert.True(row.Consistent);

        // The edge resolves into the row that already carries the number, so nothing publishes it
        // twice and the two cannot drift.
        Assert.Equal(maximum, row.MaxQueuedItemsId);
        Assert.True(WorldLookup.TryFind(world.IntVariables, row.MaxQueuedItemsId, out var capacity));
        Assert.Equal(6d, capacity.Value.ToDouble());

        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// A queue the identity registry does not hold is a fact about the save, not a shortfall.
    /// </summary>
    /// <remarks>
    /// The game registers its list variables while it initialises, so a pass that runs before that
    /// has nothing to report. Reporting it as a failed read would make every early cycle look like a
    /// broken build.
    /// </remarks>
    [Fact]
    public void AQueueTheRegistryDoesNotHoldYetIsNotAShortfall()
    {
        var report = Collector().Collect();
        var queues = report.For("action queues");

        Assert.Equal(WorldCategoryOutcome.Collected, queues.Outcome);
        Assert.Equal(0, queues.Sampled);
        Assert.True(queues.IsClean);
    }

    /// <summary>
    /// A queue type that is gone takes the queues with it and nothing else.
    /// </summary>
    [Fact]
    public void AnAbsentQueueTypeDegradesOnlyTheQueues()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid() });

        var report = Collector(("ActionableListVariable", null)).Collect();
        var queues = report.For("action queues");

        Assert.Equal(WorldCategoryOutcome.Unavailable, queues.Outcome);
        Assert.Contains("ActionableListVariable", queues.FirstFailure, StringComparison.Ordinal);
        Assert.Equal(WorldCategoryOutcome.Collected, report.For("resources").Outcome);
    }

    private static FakeCostList CostOf(params (Guid Resource, double Amount)[] entries)
    {
        var list = new FakeCostList();
        foreach (var (resource, amount) in entries) list.costs.Add(new FakeCostEntry(resource, amount));
        return list;
    }

    private static FakeModifierListRef LevelModifiers(
        FakeValueModifier[] modifiers,
        FakeValueModifier[]? exponents = null)
    {
        var list = new FakeModifierList();
        list.modifiers.AddRange(modifiers);
        if (exponents is not null) list.exponents.AddRange(exponents);
        return new FakeModifierListRef { variable = list };
    }

    /// <summary>
    /// The frame-wide rate terms are read from the player once per pass and reach the derived rows.
    /// </summary>
    /// <remarks>
    /// Asserted by changing one of them and re-collecting rather than by inspecting the frame, because
    /// what matters is that the value a consumer eventually reads moved — reading the globals into a
    /// struct nothing consults would satisfy any weaker assertion.
    /// </remarks>
    [Fact]
    public void TheFrameWideRateTermsAreReadFromThePlayerAndReachTheRows()
    {
        var mana = Guid.NewGuid();
        FakeResource.All.Add(OverflowingResource(mana));

        var collector = Collector();

        FakePlayerGlobals.SetOverflow(200d);
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var damped));

        FakePlayerGlobals.SetOverflow(100d);
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var blocked));

        Assert.NotEqual(damped.TrueRate, blocked.TrueRate);
    }

    /// <summary>
    /// A player that cannot be bound costs the globals, not the pass. They feed five terms out of a
    /// chain of a dozen; failing the whole collection over them would take away every category to
    /// protect one number.
    /// </summary>
    [Fact]
    public void AnUnbindablePlayerLeavesTheGlobalsAtZeroRatherThanFailingThePass()
    {
        var mana = Guid.NewGuid();
        FakeResource.All.Add(OverflowingResource(mana));

        var collector = Collector(("Player", null));
        var report = collector.Collect();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var row));
        Assert.False(BigDouble.IsNaN(row.TrueRate) || BigDouble.IsInfinity(row.TrueRate));
    }

    /// <summary>Over its cap with an active rate, which is the branch the frame-wide terms feed.</summary>
    private static FakeResource OverflowingResource(Guid identity) =>
        new()
        {
            Identity = identity,
            Quantity = new BigDouble(150d),
            maxQuantity = new FakeModifierRecord(100d),
            rate = new FakeModifierRecord(10d, activeCount: 1),
            lifetimeQuantity = new BigDouble(400d),
            Visible = true,
        };

    /// <summary>
    /// The player's static globals, shaped as the game holds them: an accessor returning a variable
    /// whose <c>value</c> record carries the number.
    /// </summary>
    /// <remarks>
    /// The attribute-quality bonus resets to zero because that is what the game authors it at — it is
    /// granted by research — and zero is the exponent that leaves the quality discount at one. So
    /// every test that does not speak about quality prices exactly as it did before the discount was
    /// read at all.
    /// </remarks>
    private static class FakePlayerGlobals
    {
        private static FakeGlobalVariable _overflow = new(200d);
        private static FakeGlobalVariable _overflowLoss = new(100d);
        private static FakeGlobalVariable _resetTimePassed = new(60d);
        private static FakeGlobalVariable _structureCost = new(100d);
        private static FakeGlobalVariable _attributeQualityBonus = new(0d);

        /// <summary>Back to the game's authored values, so one test cannot set another's globals.</summary>
        internal static void Reset()
        {
            _overflow = new FakeGlobalVariable(200d);
            _overflowLoss = new FakeGlobalVariable(100d);
            _resetTimePassed = new FakeGlobalVariable(60d);
            _structureCost = new FakeGlobalVariable(100d);
            _attributeQualityBonus = new FakeGlobalVariable(0d);
        }

        internal static void SetOverflow(double percent) => _overflow = new FakeGlobalVariable(percent);

        internal static void SetStructureCost(double percent) =>
            _structureCost = new FakeGlobalVariable(percent);

        internal static void SetAttributeQualityBonus(double exponent) =>
            _attributeQualityBonus = new FakeGlobalVariable(exponent);

        public static FakeGlobalVariable GetResourceOverflow() => _overflow;

        public static FakeGlobalVariable GetResourceOverflowLoss() => _overflowLoss;

        public static FakeGlobalVariable GetResetTimePassed() => _resetTimePassed;

        public static FakeGlobalVariable GetStructureCost() => _structureCost;

        public static FakeGlobalVariable GetAttributeQualityBonus() => _attributeQualityBonus;
    }

    private sealed class FakeGlobalVariable
    {
        public FakeModifierRecord value;

        internal FakeGlobalVariable(double amount) => value = new FakeModifierRecord(amount);
    }

    /// <summary>A resource shape that lost two members, standing in for a game update that moved them.</summary>
    private sealed class PartialResource
    {
        public static readonly List<PartialResource> All = new();

        public FakeModifierRecord maxQuantity = new(-1d);

        public Guid GetGuid() => Guid.NewGuid();

        public BigDouble GetQuantity() => default;
    }
}
