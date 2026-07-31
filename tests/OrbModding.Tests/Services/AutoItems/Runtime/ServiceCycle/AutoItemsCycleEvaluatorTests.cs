using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsCycleEvaluatorTests
{
    [Fact]
    public void RelicsTakePriorityAndTheEvaluatorPlansAtMostOneActionPerWorld()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var world = World(
            Consumable(scrollId, randomizable: true),
            Consumable(relicId, randomizable: false),
            new[]
            {
                new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid),
                new WorldConsumableType(relicId, KnownEntities.ConsumableRelicType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(scrollId, 3, 1, 0),
            });

        var planned = Plan(world, Configuration(), out var wake);

        var action = Assert.Single(planned);
        Assert.Equal(AutoItemsConsumableFamily.Relic, action.Family);
        Assert.Equal(relicId, action.ItemId);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void PermanentFruitRelicMembershipPlansAsRelicWithoutTemporaryApproval()
    {
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000021");
        var world = World(
            Consumable(relicId, randomizable: false),
            null,
            new[]
            {
                new WorldConsumableType(relicId, KnownEntities.ConsumableFruitType.Uuid),
                new WorldConsumableType(relicId, KnownEntities.ConsumableRelicType.Uuid),
            },
            Array.Empty<WorldConsumableCount>());

        var action = Assert.Single(Plan(world, Configuration(), out var wake));

        Assert.Equal(AutoItemsConsumableFamily.Relic, action.Family);
        Assert.Equal(relicId, action.ItemId);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void UnsupportedRelicMembershipConflictStillFailsClosed()
    {
        var itemId = Guid.Parse("00000000-0000-0000-0000-000000000022");
        var world = World(
            Consumable(itemId, randomizable: false),
            null,
            new[]
            {
                new WorldConsumableType(itemId, KnownEntities.ConsumablePotionType.Uuid),
                new WorldConsumableType(itemId, KnownEntities.ConsumableRelicType.Uuid),
            },
            Array.Empty<WorldConsumableCount>());

        var actions = Plan(world, Configuration(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void ScrollPlanningCarriesTheStrongestOwnedLevel()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var world = World(
            Consumable(scrollId, randomizable: true),
            null,
            new[]
            {
                new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid),
            },
            new[]
            {
                new WorldConsumableCount(scrollId, 1, 2, 0),
                new WorldConsumableCount(scrollId, 5, 1, 0),
            });

        var action = Assert.Single(Plan(world, Configuration(), out var wake));

        Assert.Equal(AutoItemsConsumableFamily.Scroll, action.Family);
        Assert.Equal(5, action.PlannedLevel);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void DisabledAndIdlePathsWaitForPublicationWithoutTimers()
    {
        var disabled = Configuration(AutoItemsOperationMode.Disabled);
        var world = new GameWorldState { CollectedAtEpoch = 9 };

        var disabledActions = Plan(world, disabled, out var disabledWake);
        var idleActions = Plan(world, Configuration(), out var idleWake);

        Assert.Empty(disabledActions);
        Assert.Empty(idleActions);
        Assert.Equal(WakePolicy.OnPublication, disabledWake);
        Assert.Equal(WakePolicy.OnPublication, idleWake);
    }

    [Fact]
    public void PublishedNativePreparationSuppressesEveryConsumablePlan()
    {
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000040");
        var preparingId = Guid.Parse("00000000-0000-0000-0000-000000000041");
        var world = World(
            Consumable(relicId, randomizable: false),
            Consumable(preparingId, randomizable: false, currentPrepTime: new BigDouble(2)),
            new[]
            {
                new WorldConsumableType(relicId, KnownEntities.ConsumableRelicType.Uuid),
                new WorldConsumableType(preparingId, KnownEntities.ConsumableRelicType.Uuid),
            },
            Array.Empty<WorldConsumableCount>());

        var actions = Plan(world, Configuration(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void OpenPublicationGapPreventsLateWorldFromClearingPermanentSettlement()
    {
        var itemId = Guid.NewGuid();
        var world = World(
            Consumable(itemId, randomizable: false),
            null,
            new[] { new WorldConsumableType(itemId, KnownEntities.ConsumableRelicType.Uuid) },
            Array.Empty<WorldConsumableCount>()) with
        {
            CollectedAtFrame = 12,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "consumables",
                        WorldCategoryOutcome.Collected,
                        sampled: 1,
                        skipped: 0,
                        string.Empty),
                }),
        };
        var config = Configuration();
        var identity = new ServiceCycleIdentity(
            AutoItemsServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(1));
        var state = AutoItemsCycleState.Create(identity.Lifecycle);
        var submitted = new AutoItemsCycleAction(
            itemId,
            AutoItemsConsumableFamily.Relic,
            collectedAtEpoch: 1,
            plannedLevel: 1,
            collectedAtFrame: 10);
        state.RecordSubmittedPermanent(in submitted);
        var gap = new ConsumableMutationPublicationGapCoordinator();
        gap.ObserveMutationAttempt(lifecycle: 1, mutationFrame: 13);
        var store = new ReusableActionStore<AutoItemsCycleAction>();
        store.BeginWrite();

        AutoItemsCycleEvaluator.Evaluate(
            world,
            in config,
            in context,
            ref state,
            new ServiceActionWriter<AutoItemsCycleAction>(store),
            gap,
            out var metrics);

        Assert.Equal(AutoItemsDecisionKind.AwaitingPermanentSettlement, metrics.Kind);
        Assert.Equal(itemId, state.PendingPermanentItem);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void PublishedEmptyStrongestScrollTargetSetLeavesTheScrollIdle()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000050");
        var world = World(
            Consumable(scrollId, randomizable: true),
            null,
            new[] { new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid) },
            new[] { new WorldConsumableCount(scrollId, 13, 1, 0) },
            scrollTargetCount: 0);

        var actions = Plan(world, Configuration(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void ActiveScribeWorkForTheSameScrollBlocksUsePlanningAtEveryLevel()
    {
        var scrollId = Guid.Parse("00000000-0000-0000-0000-000000000051");
        var recipeId = Guid.Parse("00000000-0000-0000-0000-000000000052");
        var world = World(
            Consumable(scrollId, randomizable: true),
            null,
            new[] { new WorldConsumableType(scrollId, KnownEntities.ConsumableScrollType.Uuid) },
            new[] { new WorldConsumableCount(scrollId, 4, 1, 0) },
            scribeRecipes: new[]
            {
                new WorldScribeRecipe(recipeId, Guid.NewGuid(), scrollId, true, true),
            },
            scribeWork: new[]
            {
                new WorldScribeWork(Guid.NewGuid(), recipeId, 9, false, false),
            });

        var actions = Plan(world, Configuration(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    private static IReadOnlyList<AutoItemsCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<AutoItemsCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoItemsCycleAction>(store);
        var identity = new ServiceCycleIdentity(
            AutoItemsServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(
            identity,
            default,
            new MonotonicTimestamp(1));
        var state = AutoItemsCycleState.Create(identity.Lifecycle);
        state.ObserveConfiguration(identity.Config, configuration.AutoItems);
        wake = AutoItemsCycleEvaluator.Evaluate(
            world,
            in configuration,
            in context,
            ref state,
            writer,
            new ConsumableMutationPublicationGapCoordinator(),
            out _);
        var actions = new List<AutoItemsCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }

    private static GameWorldState World(
        WorldConsumable first,
        WorldConsumable? second,
        WorldConsumableType[] types,
        WorldConsumableCount[] counts,
        int scrollTargetCount = 1,
        WorldScribeRecipe[]? scribeRecipes = null,
        WorldScribeWork[]? scribeWork = null)
    {
        var consumables = second.HasValue
            ? new[] { first, second.Value }
            : new[] { first };
        var effectiveCounts = new List<WorldConsumableCount>(counts);
        for (var index = 0; index < consumables.Length; index++)
        {
            var itemId = consumables[index].ConsumableId;
            var found = false;
            for (var countIndex = 0; countIndex < effectiveCounts.Count; countIndex++)
                found |= effectiveCounts[countIndex].ConsumableId == itemId;
            if (!found) effectiveCounts.Add(new WorldConsumableCount(itemId, 1, 1, 0));
        }
        var countTable = PublicationTable<WorldConsumableCount>.Create(
            effectiveCounts.ToArray());
        var costs = new WorldConsumableCost[consumables.Length];
        for (var index = 0; index < consumables.Length; index++)
        {
            costs[index] = new WorldConsumableCost(
                consumables[index].ConsumableId,
                WorldConsumableCostKind.Consume,
                KnownEntities.PotionToxicity.Uuid,
                new BigDouble(1));
        }
        var targetEvidence = new List<WorldScrollUseTargetEvidence>();
        for (var index = 0; index < consumables.Length; index++)
        {
            if (!consumables[index].CanBeRandomized ||
                !WorldConsumableCountLookup.TryGetStrongestOwnedLevel(
                    countTable,
                    consumables[index].ConsumableId,
                    out var level))
                continue;
            targetEvidence.Add(new WorldScrollUseTargetEvidence(
                consumables[index].ConsumableId,
                level,
                scrollTargetCount));
        }
        return new GameWorldState
        {
            Consumables = WorldTable.Create(consumables),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(types),
            ConsumableCounts = countTable,
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(costs),
            ScrollUseTargetEvidence = PublicationTable<WorldScrollUseTargetEvidence>.Create(
                targetEvidence.ToArray()),
            ScribeRecipes = PublicationTable<WorldScribeRecipe>.Create(
                scribeRecipes ?? Array.Empty<WorldScribeRecipe>()),
            ScribeWork = PublicationTable<WorldScribeWork>.Create(
                scribeWork ?? Array.Empty<WorldScribeWork>()),
            Resources = WorldTable.Create(Toxicity()),
            CollectedAtEpoch = 9,
        };
    }

    private static WorldConsumable Consumable(
        Guid id,
        bool randomizable,
        BigDouble currentPrepTime = default)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            id,
            visible: true,
            randomized: false,
            quantity: 1,
            queuedQuantity: 0,
            maximumCarryLoad: 10,
            gainedSince: 0,
            maxCreatedLevel: 1,
            currentPrepTime,
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 1,
            canBeRandomized: randomizable,
            hasDuration: false,
            durationBase: 0,
            queueOnStart: false);
    }

    private static WorldResource Toxicity()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = new RawResourceTraits(
            0, 0, 0, false, false, false, false, true, false, true,
            BigDouble.Zero, 0, 0, 0, false, 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            KnownEntities.PotionToxicity.Uuid,
            new BigDouble(100),
            new BigDouble(100),
            BigDouble.Zero,
            visible: true,
            lifetimeQuantity: BigDouble.Zero,
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100),
            gainRate: new BigDouble(100),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: BigDouble.Zero,
            fillFraction: 1,
            isAtCapacity: true,
            trueQuantity: new BigDouble(100),
            trueRate: BigDouble.Zero);
    }

    private static SuiteRuntimeConfiguration Configuration(
        AutoItemsOperationMode mode = AutoItemsOperationMode.Active) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = mode,
                UseRelics = true,
                UseScrolls = true,
            },
        };
}
