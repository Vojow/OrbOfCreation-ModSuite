using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuySimulationE2ETests
{
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void QueueAdapter_ReadsSharedActionQueueInsteadOfNativeAutoBuyQueue()
    {
        ActionManager.RemainingRoom = 203;
        AutoBuyManager.RemainingRoom = 11;
        using var catalog = new ReflectionAutoBuyCatalog();

        var succeeded = catalog.TryGetRemainingQueueRoom(out var remainingRoom);

        Assert.True(succeeded);
        Assert.Equal(203, remainingRoom);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LoneCandidate_FillsEveryUsableNativeQueueSlot()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            new[]
            {
                new SimulatedCandidateSpec("only-structure", AutoBuyCandidateKind.Structure),
            });

        var filled = simulation.RunUntil(
            world => world.QueueCount == 303,
            maximumFrames: 320);

        Assert.True(filled, "The single affordable candidate did not fill the simulated native queue.");
        Assert.Equal(303, simulation.World.QueueCount);
        Assert.Equal(303, simulation.World.TotalSubmitted);
        Assert.Equal(303, simulation.World.QueueHighWater);
        Assert.Equal(303, simulation.World.Candidates[0].QueuedLevels);
        Assert.Equal(0, simulation.World.TotalCompleted);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ManualActionsAndAutomationShareTheSameQueueCapacity()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });
        for (var i = 0; i < 100; i++)
        {
            simulation.World.EnqueueManualAction();
        }

        var filled = simulation.RunUntil(
            world => world.QueueCount == 303,
            maximumFrames: 220);

        Assert.True(filled, "Automation did not consume the room remaining beside manual actions.");
        Assert.Equal(303, simulation.World.QueueCount);
        Assert.Equal(203, simulation.World.TotalSubmitted);
        Assert.Equal(203, simulation.World.Candidates[0].QueuedLevels);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReload_ReplacesNativeIdentitiesAndResumesFromAuthoritativeState()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 8,
            new[]
            {
                new SimulatedCandidateSpec("always-ready", AutoBuyCandidateKind.Structure, baseCost: 10.0),
                new SimulatedCandidateSpec(
                    "unlocks-after-load",
                    AutoBuyCandidateKind.Upgrade,
                    baseCost: 10.0,
                    available: false),
            },
            initialResourceQuantity: 45.0);

        simulation.RunFrames(20);

        var first = simulation.World.Candidates[0];
        var unlockedAfterLoad = simulation.World.Candidates[1];
        Assert.Equal(4, simulation.World.TotalSubmitted);
        Assert.Equal(5.0, simulation.World.ResourceQuantity, 6);
        Assert.Equal(0, unlockedAfterLoad.PurchaseCalls);

        var firstIdentity = first.NativeIdentity;
        var secondIdentity = unlockedAfterLoad.NativeIdentity;
        simulation.World.ResourceQuantity = 100.0;
        unlockedAfterLoad.Available = true;
        simulation.ReloadLifecycle();

        Assert.NotSame(firstIdentity, first.NativeIdentity);
        Assert.NotSame(secondIdentity, unlockedAfterLoad.NativeIdentity);
        Assert.Equal(0, simulation.World.QueueCount);

        var refilled = simulation.RunUntil(
            world => world.QueueCount == 7,
            maximumFrames: 30);

        Assert.True(refilled, "Auto Buy did not resume after the simulated save/load lifecycle.");
        Assert.Contains("unlocks-after-load", simulation.World.SubmissionOrder);
        Assert.True(unlockedAfterLoad.PurchaseCalls > 0);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AmbiguousNativeFailure_FailsClosedWithoutCorruptingQueueAccounting()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 12,
            new[]
            {
                new SimulatedCandidateSpec(
                    "ambiguous",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0,
                    failureMode: SimulatedPurchaseFailureMode.MutateThenReportFailure),
                new SimulatedCandidateSpec("healthy", AutoBuyCandidateKind.Upgrade, baseCost: 20.0),
            },
            initialResourceQuantity: 100.0);

        simulation.RunFrames(1);

        var ambiguous = simulation.World.Candidates[0];
        var healthy = simulation.World.Candidates[1];
        Assert.Equal(1, ambiguous.PurchaseCalls);
        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.QueueCount);
        Assert.Equal(90.0, simulation.World.ResourceQuantity, 6);
        Assert.Equal(0, healthy.PurchaseCalls);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void PeriodicCompletions_KeepPreparedHandoffAndBoundedEvaluationWork()
    {
        var candidates = Enumerable.Range(0, 166)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + (index % 7)))
            .ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            candidates,
            initialResourceQuantity: 1_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);

        for (var frame = 0; frame < 900; frame++)
        {
            var completions = frame >= 400 && (frame - 400) % 20 == 0 ? 1 : 0;
            simulation.Step(completions);
        }

        var evaluations = simulation.World.TotalCandidateEvaluations;
        var purchases = simulation.World.TotalSubmitted;
        var evaluationBudget = (4 * candidates.Length) + (4 * purchases);

        Assert.True(simulation.World.QueueHighWater >= 300,
            $"Queue high-water was only {simulation.World.QueueHighWater}/304.");
        Assert.True(simulation.World.QueueCount >= 295,
            $"Final queue depth collapsed to {simulation.World.QueueCount}/304.");
        Assert.True(purchases >= 325,
            $"Only {purchases} purchases completed within the deterministic frame budget.");
        Assert.True(evaluations <= evaluationBudget,
            $"Candidate work grew to {evaluations} evaluations; budget was {evaluationBudget}.");
        Assert.True(simulation.Metrics.MaximumEvaluationsInFrame <= 55,
            $"One frame evaluated {simulation.Metrics.MaximumEvaluationsInFrame} candidates.");
        Assert.True(simulation.Metrics.IdleFramesWithPurchasableWork <= 90,
            $"Usable queue room was left idle for {simulation.Metrics.IdleFramesWithPurchasableWork} frames.");
        Assert.NotNull(simulation.Metrics.FramesToNinetyPercentQueue);
        Assert.Equal(166, simulation.World.SubmissionOrder.Take(166).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact(Skip = "Known completion-storm regression: repeated completion signals can restart a CPU-sliced scan before it settles.")]
    [Trait("Category", "PerformanceTarget")]
    public void CompletionStorm_TargetsNearFullQueueWithoutEvaluationAmplification()
    {
        var candidates = Enumerable.Range(0, 166)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + (index % 7)))
            .ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            candidates,
            initialResourceQuantity: 1_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);

        for (var frame = 0; frame < 900; frame++)
        {
            var completions = frame >= 400 && (frame - 400) % 4 == 0 ? 1 : 0;
            simulation.Step(completions);
        }

        var purchases = simulation.World.TotalSubmitted;
        var evaluationBudget = (4 * candidates.Length) + (4 * purchases);
        Assert.True(simulation.World.QueueCount >= 295);
        Assert.True(purchases >= 416);
        Assert.True(simulation.World.TotalCandidateEvaluations <= evaluationBudget);
        Assert.True(simulation.Metrics.IdleFramesWithPurchasableWork <= 40);
    }
}
