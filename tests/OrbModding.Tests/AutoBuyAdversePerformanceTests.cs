using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "PerformanceSimulation")]
[Trait("Category", "AutoBuyPerformance")]
[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuyAdversePerformanceTests
{
    [Fact]
    public void ScarceEconomy_RepeatedThresholdCrossingsRemainBounded()
    {
        using var simulation = CreateSimulation(candidateCount: 20, queueCapacity: 32, initialResource: 50.0);
        simulation.Config.AbsoluteReserve.Value = "50";

        for (var frame = 0; frame < 600; frame++)
        {
            if (frame % 40 == 0)
            {
                simulation.World.ResourceQuantity = 100.0;
                simulation.NotifyProgressionChanged();
            }

            simulation.Step(completionsBeforeTick: frame % 2 == 0 ? 1 : 0);
            Assert.True(simulation.World.ResourceQuantity >= 50.0);
        }

        Assert.True(simulation.World.TotalSubmitted > 0);
        AssertBounded(simulation, evaluationBudget: 6_000);
    }

    [Fact]
    public void LockedCatalog_AvailableCandidatesProgressWithoutScanningExplosion()
    {
        using var simulation = CreateSimulation(candidateCount: 40, queueCapacity: 64);
        foreach (var candidate in simulation.World.Candidates.Take(10))
        {
            candidate.Available = false;
        }

        simulation.NotifyProgressionChanged();
        simulation.RunFrames(800, completionsPerFrame: 1);

        Assert.DoesNotContain(
            simulation.World.SubmissionOrder,
            uuid => int.Parse(uuid.AsSpan("adverse-".Length)) < 10);
        Assert.True(simulation.World.DistinctCandidatesSubmitted >= 30);
        AssertBounded(simulation, evaluationBudget: 12_000);
    }

    [Fact(Skip = "Known baseline gap: a permanently rejecting cheapest candidate is immediately reranked and can starve healthy lower ranks; retain this gate for the retry/quarantine change.")]
    public void PermanentlyFailingLeaders_DoNotStarveHealthyLowerRanks()
    {
        using var simulation = CreateSimulation(candidateCount: 40, queueCapacity: 64);
        foreach (var candidate in simulation.World.Candidates.Take(2))
        {
            candidate.FailureMode = SimulatedPurchaseFailureMode.RejectBeforeMutation;
        }

        simulation.RunFrames(800, completionsPerFrame: 1);

        Assert.True(simulation.World.DistinctCandidatesSubmitted >= 38);
        Assert.All(simulation.World.Candidates.Take(2), candidate =>
            Assert.True(candidate.PurchaseCalls <= 40));
        AssertBounded(simulation, evaluationBudget: 12_000);
    }

    [Fact]
    public void ValidCapacityOscillation_NeverOverfillsAndConsumesExpandedRoom()
    {
        using var simulation = CreateSimulation(candidateCount: 40, queueCapacity: 64);
        Assert.True(simulation.RunUntil(world => world.QueueCount >= 60, maximumFrames: 100));

        simulation.World.SetQueueCapacity(128);
        Assert.True(simulation.RunUntil(world => world.QueueCount >= 120, maximumFrames: 100));

        simulation.Catalog.QueueReadSucceeds = false;
        while (simulation.World.QueueCount > 32)
        {
            simulation.Step(completionsBeforeTick: 4);
        }

        simulation.World.SetQueueCapacity(64);
        simulation.Catalog.QueueReadSucceeds = true;
        Assert.True(simulation.RunUntil(world => world.QueueCount >= 60, maximumFrames: 80));

        simulation.Catalog.QueueReadSucceeds = false;
        while (simulation.World.QueueCount > 32)
        {
            simulation.Step(completionsBeforeTick: 4);
        }

        simulation.World.SetQueueCapacity(304);
        simulation.Catalog.QueueReadSucceeds = true;
        Assert.True(simulation.RunUntil(world => world.QueueCount >= 280, maximumFrames: 300));

        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
        AssertBounded(simulation, evaluationBudget: 16_000);
    }

    [Fact]
    public void ManualBursts_PreserveManualOwnershipAndAutomationRecovers()
    {
        using var simulation = CreateSimulation(candidateCount: 24, queueCapacity: 48);

        for (var frame = 0; frame < 800; frame++)
        {
            if (frame % 25 == 0)
            {
                simulation.World.TryEnqueueManualActions(2, out _);
            }

            simulation.Step(completionsBeforeTick: frame % 3 == 0 ? 1 : 0);
            Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
        }

        Assert.True(simulation.World.TotalManualCompleted + simulation.World.ManualQueueCount > 0);
        Assert.True(simulation.World.TotalSubmitted > 0);
        AssertBounded(simulation, evaluationBudget: 12_000);
    }

    [Fact]
    public void CompletionBurstsAndGaps_KeepSettlementAndEvaluationWorkBounded()
    {
        using var simulation = CreateSimulation(candidateCount: 32, queueCapacity: 64);

        for (var cycle = 0; cycle < 20; cycle++)
        {
            simulation.RunFrames(20);
            simulation.RunFrames(10, completionsPerFrame: 3);
        }

        Assert.True(simulation.World.TotalAutomationCompleted > 0);
        AssertBounded(simulation, evaluationBudget: 14_000);
    }

    [Fact]
    public void LifecycleInterruption_DiscardsStaleWorkAndFreshWrappersResume()
    {
        using var simulation = CreateSimulation(candidateCount: 24, queueCapacity: 48);
        simulation.RunFrames(300, completionsPerFrame: 1);
        var stale = simulation.World.Candidates.ToArray();
        var submittedBeforeReload = simulation.World.TotalSubmitted;

        simulation.ReloadLifecycle(
            clearQueue: true,
            replaceNativeIdentities: false,
            replaceCandidateWrappers: true);
        simulation.RunFrames(300, completionsPerFrame: 1);

        Assert.True(simulation.World.TotalSubmitted > submittedBeforeReload);
        Assert.All(simulation.World.Candidates, candidate => Assert.DoesNotContain(candidate, stale));
        AssertBounded(simulation, evaluationBudget: 14_000);
    }

    [Fact]
    public void ResourceObservationOutage_HasZeroUnsafeMutationAndBoundedRecovery()
    {
        using var simulation = CreateSimulation(candidateCount: 24, queueCapacity: 48);
        simulation.RunFrames(150, completionsPerFrame: 1);
        var submissionsBeforeOutage = simulation.World.TotalSubmitted;

        foreach (var candidate in simulation.World.Candidates)
        {
            candidate.CostObservationMode = SimulatedCostObservationMode.Unresolved;
        }
        simulation.NotifyProgressionChanged();
        simulation.RunFrames(150, completionsPerFrame: 1);

        Assert.Equal(submissionsBeforeOutage, simulation.World.TotalSubmitted);

        foreach (var candidate in simulation.World.Candidates)
        {
            candidate.CostObservationMode = SimulatedCostObservationMode.Normal;
        }
        simulation.NotifyProgressionChanged();

        Assert.True(simulation.RunUntil(
            world => world.TotalSubmitted > submissionsBeforeOutage,
            maximumFrames: 120,
            completionsPerFrame: 1));
        AssertBounded(simulation, evaluationBudget: 12_000);
    }

    private static AutoBuySimulation CreateSimulation(
        int candidateCount,
        int queueCapacity,
        double initialResource = 1_000_000_000.0)
    {
        var specs = Enumerable.Range(0, candidateCount)
            .Select(index => new SimulatedCandidateSpec(
                $"adverse-{index:00}",
                index % 5 == 0 ? AutoBuyCandidateKind.Upgrade : AutoBuyCandidateKind.Structure,
                baseCost: 1.0 + index,
                costScaling: 1.001,
                maximumLevel: index % 5 == 0 ? 1 : 100))
            .ToArray();
        var simulation = new AutoBuySimulation(queueCapacity, specs, initialResource);
        simulation.Config.LeaveQueueSlots.Value = 0;
        return simulation;
    }

    private static void AssertBounded(AutoBuySimulation simulation, int evaluationBudget)
    {
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
        Assert.True(simulation.World.TotalAutomationCompleted <= simulation.World.TotalSubmitted);
        Assert.True(simulation.Metrics.MaximumPurchasesInFrame <= 1);
        Assert.True(simulation.Metrics.MaximumEvaluationsInFrame <= 55);
        Assert.True(simulation.World.TotalCandidateEvaluations <= evaluationBudget,
            $"Used {simulation.World.TotalCandidateEvaluations} candidate evaluations; budget was {evaluationBudget}.");
    }
}
