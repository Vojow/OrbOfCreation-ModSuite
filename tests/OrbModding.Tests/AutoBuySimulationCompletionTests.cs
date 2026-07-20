using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuySimulationCompletionTests
{
    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionCountExceedingQueuedLevels_IsRejectedAtomically()
    {
        using var simulation = CreateFilledSimulation();
        var candidate = simulation.World.Candidates[0];
        var queueBefore = simulation.World.QueueCount;

        var completed = simulation.World.TryCompleteExact(
            candidate.Uuid,
            candidate.Kind,
            queueBefore + 1,
            out var reason);

        Assert.False(completed);
        Assert.NotEmpty(reason);
        Assert.Equal(queueBefore, simulation.World.QueueCount);
        Assert.Equal(0, simulation.World.TotalCompleted);
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionIdentityMismatch_IsRejectedAtomically(bool wrongUuid)
    {
        using var simulation = CreateFilledSimulation();
        var candidate = simulation.World.Candidates[0];
        var uuid = wrongUuid ? "not-the-front" : candidate.Uuid;
        var kind = wrongUuid ? candidate.Kind : AutoBuyCandidateKind.Upgrade;
        var queueBefore = simulation.World.QueueCount;

        var completed = simulation.World.TryCompleteExact(uuid, kind, 1, out var reason);

        Assert.False(completed);
        Assert.Contains("not", reason);
        Assert.Equal(queueBefore, simulation.World.QueueCount);
        Assert.Equal(0, simulation.World.TotalCompleted);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ManualQueueFront_RejectsExactAutomationCompletionWithoutRemovingIt()
    {
        using var simulation = new AutoBuySimulation(
            4,
            new[] { new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure) });
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.World.EnqueueManualAction();
        simulation.RunFrames(3);
        var candidate = simulation.World.Candidates[0];
        var queueBefore = simulation.World.QueueCount;

        Assert.False(simulation.World.TryCompleteExact(
            candidate.Uuid,
            candidate.Kind,
            1,
            out var reason));

        Assert.Contains("manual", reason);
        Assert.Equal(queueBefore, simulation.World.QueueCount);
        Assert.Equal(1, simulation.World.ManualQueueCount);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionSignalCrossingLifecycleReplacement_CannotMutateStaleWrapper()
    {
        using var simulation = CreateFilledSimulation();
        var stale = simulation.World.Candidates[0];
        var stalePurchaseCalls = stale.PurchaseCalls;

        var observation = simulation.StepNativeCompletion(
            bulkLevels: 1,
            afterSignalBeforeOuterUnstack: _ =>
            {
                simulation.ReloadLifecycle(
                    clearQueue: true,
                    replaceNativeIdentities: false,
                    replaceCandidateWrappers: true);
                simulation.SetEmergencyDisabled(true);
            });

        Assert.True(observation.AutomationCompletion);
        Assert.Equal(1, simulation.LifecycleGeneration);
        Assert.Equal(stalePurchaseCalls, stale.PurchaseCalls);
        Assert.Equal(0, simulation.World.QueueCount);

        simulation.SetEmergencyDisabled(false);
        Assert.True(simulation.RunUntil(world => world.QueueCount > 0, maximumFrames: 30));
        Assert.NotSame(stale, simulation.World.Candidates[0]);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void MalformedCompletionBetweenValidObservations_IsAtomicAndDoesNotPoisonLaterWork()
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec("a", AutoBuyCandidateKind.Structure, maximumLevel: 1),
            new SimulatedCandidateSpec("b", AutoBuyCandidateKind.Upgrade, maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(4, specs);
        simulation.Config.LeaveQueueSlots.Value = 0;
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 2, maximumFrames: 20));
        Assert.Equal(new[] { "a", "b" }, simulation.World.SubmissionOrder.Take(2));

        Assert.True(simulation.World.TryCompleteExact(
            "a", AutoBuyCandidateKind.Structure, 1, out _));
        var queueAfterFirst = simulation.World.QueueCount;
        Assert.False(simulation.World.TryCompleteExact(
            "a", AutoBuyCandidateKind.Structure, 1, out _));
        Assert.Equal(queueAfterFirst, simulation.World.QueueCount);
        Assert.True(simulation.World.TryCompleteExact(
            "b", AutoBuyCandidateKind.Upgrade, 1, out _));

        Assert.Equal(0, simulation.World.QueueCount);
        Assert.Equal(2, simulation.World.TotalAutomationCompleted);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void QueueClearDuringActiveSettlement_ResetsStateWithoutStaleRemoval()
    {
        using var simulation = CreateFilledSimulation();
        var observation = simulation.World.BeginNativeCompletion(1, 0);
        Assert.True(observation.AutomationCompletion);

        simulation.World.ClearQueueForReload();
        simulation.World.FinishNativeCompletion();

        Assert.Equal(0, simulation.World.QueueCount);
        Assert.Equal(0, simulation.World.Candidates[0].QueuedLevels);
        var emptyObservation = simulation.World.BeginNativeCompletion(1, 0);
        Assert.False(emptyObservation.AutomationCompletion);
    }

    private static AutoBuySimulation CreateFilledSimulation()
    {
        var simulation = new AutoBuySimulation(
            4,
            new[] { new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure) });
        simulation.Config.LeaveQueueSlots.Value = 0;
        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        return simulation;
    }
}
