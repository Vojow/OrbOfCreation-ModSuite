using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuySimulationContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InitialCapacityMustBePositive(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AutoBuySimulation(capacity, Array.Empty<SimulatedCandidateSpec>()));
    }

    [Fact]
    public void CapacityUpdateCannotBeNegative()
    {
        var world = new SimulatedAutoBuyWorld(1, 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetQueueCapacity(-1));
        Assert.Equal(1, world.QueueCapacity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResourceIdentityCannotBeBlank(string resourceId)
    {
        var world = new SimulatedAutoBuyWorld(1, 1.0);
        Assert.Throws<ArgumentException>(() =>
            world.SetResourceQuantity(resourceId, new BigAmount(1.0, 0)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void InvalidManualActionCountIsRejectedAtomically(int count)
    {
        var world = new SimulatedAutoBuyWorld(4, 1.0);
        Assert.False(world.TryEnqueueManualActions(count, out var reason));
        Assert.NotEmpty(reason);
        Assert.Equal(0, world.QueueCount);
    }

    [Fact]
    public void ManualBatchBeyondRoomIsRejectedAtomically()
    {
        var world = new SimulatedAutoBuyWorld(2, 1.0);
        Assert.False(world.TryEnqueueManualActions(3, out var reason));
        Assert.NotEmpty(reason);
        Assert.Equal(0, world.QueueCount);
    }

    [Fact]
    public void ManualEnqueueBeyondRoomThrowsWithoutChangingQueue()
    {
        var world = new SimulatedAutoBuyWorld(1, 1.0);
        world.EnqueueManualAction();
        Assert.Throws<InvalidOperationException>(() => world.EnqueueManualAction());
        Assert.Equal(1, world.QueueCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void InvalidExactCompletionCountIsRejectedAtomically(int count)
    {
        var world = new SimulatedAutoBuyWorld(2, 1.0);
        Assert.False(world.TryCompleteExact(
            "candidate", AutoBuyCandidateKind.Structure, count, out var reason));
        Assert.NotEmpty(reason);
        Assert.Equal(0, world.QueueCount);
    }

    [Fact]
    public void NestedNativeCompletionThrowsButOuterCompletionRemainsFinishable()
    {
        using var simulation = CreateOneQueuedSimulation();
        var observation = simulation.World.BeginNativeCompletion(1, 0);
        Assert.True(observation.AutomationCompletion);

        Assert.Throws<InvalidOperationException>(() =>
            simulation.World.BeginNativeCompletion(1, 0));
        simulation.World.FinishNativeCompletion();

        Assert.Equal(0, simulation.World.QueueCount);
        var empty = simulation.World.BeginNativeCompletion(1, 0);
        Assert.False(empty.AutomationCompletion);
    }

    [Fact]
    public void NegativeEchoCountThrowsBeforeStateChange()
    {
        using var simulation = CreateOneQueuedSimulation();
        var queueBefore = simulation.World.QueueCount;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            simulation.World.BeginNativeCompletion(1, -1));
        Assert.Equal(queueBefore, simulation.World.QueueCount);
    }

    [Fact]
    public void ExactCompletionMismatchLeavesQueueAndCountersUnchanged()
    {
        using var simulation = CreateOneQueuedSimulation();
        Assert.False(simulation.World.TryCompleteExact(
            "wrong", AutoBuyCandidateKind.Structure, 1, out _));
        Assert.Equal(1, simulation.World.QueueCount);
        Assert.Equal(0, simulation.World.TotalCompleted);
    }

    [Fact]
    public void SimulationOperationsAfterDisposalThrowConsistently()
    {
        var simulation = new AutoBuySimulation(
            2,
            new[] { new SimulatedCandidateSpec("candidate", AutoBuyCandidateKind.Structure) });
        simulation.Dispose();

        Assert.Throws<ObjectDisposedException>(() => simulation.Step());
        Assert.Throws<ObjectDisposedException>(() => simulation.ReloadLifecycle());
        Assert.Throws<ObjectDisposedException>(() => simulation.NotifyProgressionChanged());
        Assert.Throws<ObjectDisposedException>(() => simulation.SetEmergencyDisabled(true));
        Assert.Throws<ObjectDisposedException>(() =>
            simulation.SetResourceQuantity("resource", new BigAmount(1.0, 0)));
    }

    private static AutoBuySimulation CreateOneQueuedSimulation()
    {
        var simulation = new AutoBuySimulation(
            2,
            new[] { new SimulatedCandidateSpec("candidate", AutoBuyCandidateKind.Structure) });
        simulation.Config.LeaveQueueSlots.Value = 0;
        Assert.True(simulation.RunUntil(world => world.QueueCount == 1, maximumFrames: 20));
        return simulation;
    }
}
