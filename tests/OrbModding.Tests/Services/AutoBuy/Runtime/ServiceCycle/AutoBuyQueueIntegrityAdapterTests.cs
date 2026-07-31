using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyQueueIntegrityAdapterTests : IDisposable
{
    public AutoBuyQueueIntegrityAdapterTests() => global::ActionManager.ResetTestState();

    public void Dispose() => global::ActionManager.ResetTestState();

    [Fact]
    public void MatchingExactStructureStackIsHealthy()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            queuedQuantity = 3,
        };
        var queue = global::ActionManager.instance.actionableItems;
        queue.maxQueuedItems.Value = 132;
        queue.Stack(structure, 3);

        var read = new AutoBuyNativeQueueIntegrityAdapter()
            .TryReadHealthy(out var healthy, out _);

        Assert.True(read);
        Assert.True(healthy);
    }

    [Fact]
    public void OneExcessStructureStackIsContradictory()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            queuedQuantity = 2,
        };
        var queue = global::ActionManager.instance.actionableItems;
        queue.maxQueuedItems.Value = 132;
        queue.Stack(structure, 3);

        var read = new AutoBuyNativeQueueIntegrityAdapter()
            .TryReadHealthy(out var healthy, out var reason);

        Assert.True(read);
        Assert.False(healthy);
        Assert.Contains("stacks=3, pending=2", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingExactUpgradeStackIsHealthy()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            level = 7,
            queuedLevels = 2,
        };
        var queue = global::ActionManager.instance.actionableItems;
        queue.maxQueuedItems.Value = 132;
        queue.Stack(upgrade, 2);

        var read = new AutoBuyNativeQueueIntegrityAdapter()
            .TryReadHealthy(out var healthy, out _);

        Assert.True(read);
        Assert.True(healthy);
    }

    [Fact]
    public void NativeRecoveryRemovesOnlyTheExactProvenExcess()
    {
        const long lifecycle = 7;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            queuedQuantity = 2,
        };
        var queue = global::ActionManager.instance.actionableItems;
        queue.SetGuid(KnownEntities.ActiveActionables.Uuid);
        queue.maxQueuedItems.Value = 132;
        queue.Stack(structure, 3);
        var observation = new ActionQueueMemberObservation(
            lifecycle,
            1,
            KnownEntities.ActiveActionables.Uuid,
            structure.GetGuid(),
            "StructureSO",
            memberStacks: 3,
            authoritativePending: 2,
            totalStacks: 3,
            remainingRoom: 129,
            observedAfterRestart: false);
        var finding = ActionQueueIntegrityClassifier.Classify(in observation);
        var ticket = new ActionQueueRecoveryTicket(
            Guid.NewGuid(),
            in observation,
            in finding);
        var action = new ActionQueueRecoveryGameAction(
            new ActionQueueNativeRecoveryAdapter(() => true, () => lifecycle));

        var result = action.Execute(in ticket);

        Assert.True(result.IsCommitted);
        Assert.Equal(1, result.UnloadedStacks);
        Assert.Equal(2, queue.GetStacks(structure));
        Assert.Equal(2, structure.GetQueuedQuantity());
        Assert.Equal(2, queue.GetTotalStacks());
        Assert.Equal(130, queue.GetRemainingRoom());
    }
}
