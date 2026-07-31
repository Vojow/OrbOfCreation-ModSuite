using System;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsPermanentSettlementPolicyTests
{
    [Fact]
    public void StaleAndIncompleteConsumablesPublicationsWaitWithoutInspectingTopology()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);
        var worlds = new[]
        {
            World(itemId, frame: 10, category: CategoryKind.Clean, includeItem: false),
            World(itemId, frame: 11, category: CategoryKind.Missing, includeItem: false),
            World(itemId, frame: 11, category: CategoryKind.Unavailable, includeItem: false),
            World(itemId, frame: 11, category: CategoryKind.Skipped, includeItem: false),
        };

        foreach (var world in worlds)
        {
            var observation = AutoItemsPermanentSettlementPolicy.Observe(world, ref state);

            Assert.Equal(
                AutoItemsPermanentSettlementState.AwaitingSettlement,
                observation.State);
            Assert.Equal(AutoItemsPermanentQuarantineCause.None, observation.QuarantineCause);
            Assert.Equal(itemId, state.PendingPermanentItem);
        }
    }

    [Fact]
    public void ExactPendingUsageAndPreparationAreAcceptedAsPreparing()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, plannedLevel: 3);

        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(
                itemId,
                queue: 1,
                preparation: 2,
                usages: new[] { Usage(itemId, level: 3, engaged: false, remaining: 1) }),
            ref state);

        Assert.Equal(AutoItemsPermanentSettlementState.AwaitingSettlement, observation.State);
        Assert.True(state.PermanentUsageSeen);
        Assert.Equal(itemId, state.PendingPermanentItem);
    }

    [Fact]
    public void FirstFreshPublicationMayAlreadyBeDrained()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId);

        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(itemId, queue: 0, preparation: -1),
            ref state);

        Assert.Equal(AutoItemsPermanentSettlementState.Completed, observation.State);
        Assert.Equal(itemId, observation.ItemId);
        Assert.Equal(Guid.Empty, state.PendingPermanentItem);
        Assert.Equal(AutoItemsPermanentQuarantineCause.None, observation.QuarantineCause);
    }

    [Fact]
    public void WrongUsageLevelQuarantinesThePermanentFamily()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, plannedLevel: 3);

        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(
                itemId,
                queue: 1,
                preparation: 2,
                usages: new[] { Usage(itemId, level: 2, engaged: false, remaining: 1) }),
            ref state);

        AssertQuarantined(
            observation,
            state,
            itemId,
            AutoItemsPermanentQuarantineCause.UsageLevelChanged);
    }

    [Fact]
    public void MultipleUsagesQuarantine()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId);

        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(
                itemId,
                queue: 1,
                preparation: 2,
                usages: new[]
                {
                    Usage(itemId, level: 3, engaged: false, remaining: 1),
                    Usage(itemId, level: 3, engaged: false, remaining: 1),
                }),
            ref state);

        AssertQuarantined(
            observation,
            state,
            itemId,
            AutoItemsPermanentQuarantineCause.MultipleUsages);
    }

    [Fact]
    public void EngagedAndExpiredUsagesQuarantineWithExactCauses()
    {
        var itemId = Guid.NewGuid();
        var engagedState = SubmittedState(itemId);
        var expiredState = SubmittedState(itemId);

        var engaged = AutoItemsPermanentSettlementPolicy.Observe(
            World(
                itemId,
                queue: 1,
                preparation: 2,
                usages: new[] { Usage(itemId, level: 3, engaged: true, remaining: 1) }),
            ref engagedState);
        var expired = AutoItemsPermanentSettlementPolicy.Observe(
            World(
                itemId,
                queue: 1,
                preparation: 2,
                usages: new[] { Usage(itemId, level: 3, engaged: true, remaining: 0) }),
            ref expiredState);

        AssertQuarantined(
            engaged,
            engagedState,
            itemId,
            AutoItemsPermanentQuarantineCause.EngagedUsage);
        AssertQuarantined(
            expired,
            expiredState,
            itemId,
            AutoItemsPermanentQuarantineCause.ExpiredUsage);
    }

    [Fact]
    public void QueueOutsideNativeSingleSubmissionShapeQuarantines()
    {
        var itemId = Guid.NewGuid();
        var overflowState = SubmittedState(itemId);
        var negativeState = SubmittedState(itemId);

        var overflow = AutoItemsPermanentSettlementPolicy.Observe(
            World(itemId, queue: 2, preparation: 2),
            ref overflowState);
        var negative = AutoItemsPermanentSettlementPolicy.Observe(
            World(itemId, queue: -1, preparation: 0),
            ref negativeState);

        AssertQuarantined(
            overflow,
            overflowState,
            itemId,
            AutoItemsPermanentQuarantineCause.QueueOverflow);
        AssertQuarantined(
            negative,
            negativeState,
            itemId,
            AutoItemsPermanentQuarantineCause.InvalidQueue);
    }

    [Fact]
    public void QueuePreparationAndUsageContradictionsQuarantine()
    {
        var itemId = Guid.NewGuid();
        AssertTopologyCause(
            itemId,
            queue: 1,
            preparation: 2,
            expected: AutoItemsPermanentQuarantineCause.MissingUsageDuringPreparation);
        AssertTopologyCause(
            itemId,
            queue: 1,
            preparation: 0,
            expected: AutoItemsPermanentQuarantineCause.QueueStuckWithoutUsage);
        AssertTopologyCause(
            itemId,
            queue: 1,
            preparation: 0,
            expected: AutoItemsPermanentQuarantineCause.PreparationMissingDuringQueuedUsage,
            usages: new[] { Usage(itemId, level: 3, engaged: false, remaining: 1) });
        AssertTopologyCause(
            itemId,
            queue: 0,
            preparation: 0,
            expected: AutoItemsPermanentQuarantineCause.QueueClearedWhileUsagePresent,
            usages: new[] { Usage(itemId, level: 3, engaged: false, remaining: 1) });
        AssertTopologyCause(
            itemId,
            queue: 0,
            preparation: 2,
            expected: AutoItemsPermanentQuarantineCause.PreparationWithoutQueue);
    }

    [Fact]
    public void MissingItemQuarantinesOnlyWhenConsumablesCollectionIsComplete()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId);

        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(itemId, category: CategoryKind.Clean, includeItem: false),
            ref state);

        AssertQuarantined(
            observation,
            state,
            itemId,
            AutoItemsPermanentQuarantineCause.ItemDisappeared);
    }

    private static void AssertTopologyCause(
        Guid itemId,
        int queue,
        double preparation,
        AutoItemsPermanentQuarantineCause expected,
        params WorldConsumableUsage[] usages)
    {
        var state = SubmittedState(itemId);
        var observation = AutoItemsPermanentSettlementPolicy.Observe(
            World(itemId, queue: queue, preparation: preparation, usages: usages),
            ref state);

        AssertQuarantined(observation, state, itemId, expected);
    }

    private static void AssertQuarantined(
        AutoItemsPermanentSettlementObservation observation,
        AutoItemsCycleState state,
        Guid itemId,
        AutoItemsPermanentQuarantineCause expected)
    {
        Assert.Equal(AutoItemsPermanentSettlementState.Quarantined, observation.State);
        Assert.Equal(expected, observation.QuarantineCause);
        Assert.Equal(itemId, observation.ItemId);
        Assert.Equal(itemId, state.LastQuarantinedPermanentItem);
        Assert.Equal(Guid.Empty, state.PendingPermanentItem);
        Assert.True(state.ScrollSettlementQuarantined);
    }

    private static AutoItemsCycleState SubmittedState(
        Guid itemId,
        int plannedLevel = 3,
        long submittedFrame = 10)
    {
        var state = AutoItemsCycleState.Create(new LifecycleGeneration(1));
        var action = new AutoItemsCycleAction(
            itemId,
            AutoItemsConsumableFamily.Scroll,
            collectedAtEpoch: 1,
            plannedLevel,
            collectedAtFrame: submittedFrame);
        state.RecordSubmittedPermanent(in action);
        return state;
    }

    private static GameWorldState World(
        Guid itemId,
        long frame = 11,
        CategoryKind category = CategoryKind.Clean,
        bool includeItem = true,
        int queue = 0,
        double preparation = 0,
        params WorldConsumableUsage[] usages) =>
        new()
        {
            CollectedAtFrame = frame,
            CollectedAtEpoch = 1,
            CollectionCategories = CategoryTable(category),
            Consumables = includeItem
                ? WorldTable.Create(Consumable(itemId, queue, preparation))
                : PublicationTable<WorldConsumable>.Empty,
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(usages),
        };

    private static PublicationTable<WorldCollectionCategoryStatus> CategoryTable(
        CategoryKind category) =>
        category switch
        {
            CategoryKind.Missing => PublicationTable<WorldCollectionCategoryStatus>.Empty,
            CategoryKind.Clean => PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        AutoItemsPermanentSettlementPolicy.CollectionCategory,
                        WorldCategoryOutcome.Collected,
                        sampled: 1,
                        skipped: 0,
                        firstFailure: string.Empty),
                }),
            CategoryKind.Unavailable => PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        AutoItemsPermanentSettlementPolicy.CollectionCategory,
                        WorldCategoryOutcome.Unavailable,
                        sampled: 0,
                        skipped: 0,
                        firstFailure: "unavailable"),
                }),
            CategoryKind.Skipped => PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        AutoItemsPermanentSettlementPolicy.CollectionCategory,
                        WorldCategoryOutcome.Collected,
                        sampled: 1,
                        skipped: 1,
                        firstFailure: "skipped"),
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private static WorldConsumable Consumable(Guid itemId, int queue, double preparation)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            itemId,
            visible: true,
            randomized: false,
            quantity: 1,
            queuedQuantity: queue,
            maximumCarryLoad: 10,
            gainedSince: 0,
            maxCreatedLevel: 3,
            currentPrepTime: new BigDouble(preparation),
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 2,
            canBeRandomized: true,
            hasDuration: false,
            durationBase: 0,
            queueOnStart: false);
    }

    private static WorldConsumableUsage Usage(
        Guid itemId,
        int level,
        bool engaged,
        double remaining) =>
        new(
            itemId,
            Guid.NewGuid(),
            level,
            engaged,
            new BigDouble(remaining),
            BigDouble.One);

    private enum CategoryKind
    {
        Missing,
        Clean,
        Unavailable,
        Skipped,
    }
}
