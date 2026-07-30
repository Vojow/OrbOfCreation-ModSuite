using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
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

    private static IReadOnlyList<AutoItemsCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<AutoItemsCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoItemsCycleAction>(store);
        wake = AutoItemsCycleEvaluator.Evaluate(
            world,
            in configuration,
            writer,
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
        WorldConsumableCount[] counts)
    {
        var consumables = second.HasValue
            ? new[] { first, second.Value }
            : new[] { first };
        var costs = new WorldConsumableCost[consumables.Length];
        for (var index = 0; index < consumables.Length; index++)
        {
            costs[index] = new WorldConsumableCost(
                consumables[index].ConsumableId,
                WorldConsumableCostKind.Consume,
                KnownEntities.PotionToxicity.Uuid,
                new BigDouble(1));
        }
        return new GameWorldState
        {
            Consumables = WorldTable.Create(consumables),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(types),
            ConsumableCounts = PublicationTable<WorldConsumableCount>.Create(counts),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(costs),
            Resources = WorldTable.Create(Toxicity()),
            CollectedAtEpoch = 9,
        };
    }

    private static WorldConsumable Consumable(Guid id, bool randomizable)
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
            currentPrepTime: BigDouble.Zero,
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
