using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyWorldQueueIntegrityTests
{
    [Fact]
    public void CleanStackedQueueIsHealthy()
    {
        var memberId = Guid.NewGuid();
        var world = World(stackCount: 3, pending: 3, memberId);

        Assert.True(AutoBuyWorldQueueIntegrity.IsHealthy(world, out _));
    }

    [Fact]
    public void ExcessStackBlocksPlanning()
    {
        var memberId = Guid.NewGuid();
        var world = World(stackCount: 3, pending: 2, memberId);

        Assert.False(AutoBuyWorldQueueIntegrity.IsHealthy(world, out var reason));
        Assert.Contains(memberId.ToString("D"), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingQueueInCollectedCategoryFailsClosed()
    {
        var world = new GameWorldState
        {
            CollectionCategories = Categories(),
        };

        Assert.False(AutoBuyWorldQueueIntegrity.IsHealthy(world, out _));
    }

    private static GameWorldState World(int stackCount, int pending, Guid memberId)
    {
        var queueId = KnownEntities.ActiveActionables.Uuid;
        return new GameWorldState
        {
            CollectionCategories = Categories(),
            ActionQueues = WorldTable.Create(new WorldActionQueue(
                queueId,
                Guid.NewGuid(),
                WorldActionQueueKind.Stacked,
                slotCount: 1,
                usedSlots: 1,
                emptySlots: 0,
                hasEmptySlot: false,
                totalStacks: stackCount,
                remainingStackRoom: 132 - stackCount,
                hasStackRoom: stackCount < 132,
                consistent: true)),
            ActionQueueMembers = PublicationTable<WorldActionQueueMember>.Create(
                new[]
                {
                    new WorldActionQueueMember(
                        queueId,
                        0,
                        memberId,
                        WorldActionQueueMemberKind.Structure,
                        stackCount,
                        pending,
                        new BigDouble(1d),
                        new BigDouble(2d),
                        new BigDouble(3d),
                        timingReadable: true),
                },
                1),
        };
    }

    private static PublicationTable<WorldCollectionCategoryStatus> Categories() =>
        PublicationTable<WorldCollectionCategoryStatus>.Create(
            new[]
            {
                new WorldCollectionCategoryStatus(
                    "action queues",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
            },
            1);
}
