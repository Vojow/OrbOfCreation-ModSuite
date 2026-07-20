using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "PerformanceSimulation")]
[Trait("Category", "AutoBuyPerformance")]
public sealed class AutoBuyRuntimeDerivedSimulationTests
{
    private const int RuntimeCatalogSize = 137;
    private const int EndgameStructureCount = 180;
    private const int EndgameUpgradeCount = 24;

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(100)]
    public void LowBulkEndgamePerformance_RemainsFairAndBounded(int bulkDevelopment)
    {
        var specs = BuildEndgameCandidates().ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            specs,
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);
        simulation.Catalog.BulkDevelopment = bulkDevelopment;

        for (var frame = 0; frame < 8_000; frame++)
        {
            simulation.Step(completionsBeforeTick: frame >= 400 ? 1 : 0);
        }

        var structures = simulation.World.Candidates
            .Where(candidate => candidate.Kind == AutoBuyCandidateKind.Structure)
            .ToArray();
        var upgrades = simulation.World.Candidates
            .Where(candidate => candidate.Kind == AutoBuyCandidateKind.Upgrade)
            .ToArray();
        var evaluationsPerPurchase =
            (double)simulation.World.TotalCandidateEvaluations / simulation.World.TotalSubmitted;

        Assert.Equal(specs.Length, simulation.World.DistinctCandidatesSubmitted);
        Assert.All(structures, structure => Assert.True(Progress(structure) >= 20));
        Assert.All(upgrades, upgrade => Assert.Equal(1, Progress(upgrade)));
        Assert.True(simulation.World.TotalSubmitted >= 7_500,
            $"Bulk {bulkDevelopment} submitted only {simulation.World.TotalSubmitted} levels.");
        Assert.True(evaluationsPerPurchase <= 75.0,
            $"Bulk {bulkDevelopment} used {evaluationsPerPurchase:F3} candidate evaluations per purchase.");
        AssertBoundedWorld(simulation);
    }

    [Fact]
    [Trait("Category", "AutoBuyReliability")]
    public void MixedTransientCostOutages_IsolateCandidatesAndRecoverEachCohort()
    {
        using var simulation = CreateRuntimeCatalogSimulation(maximumLevel: 100);
        var cohorts = new[]
        {
            simulation.World.Candidates.Where((_, index) => index % 19 == 0).ToArray(),
            simulation.World.Candidates.Where((_, index) => index % 19 == 6).ToArray(),
            simulation.World.Candidates.Where((_, index) => index % 19 == 12).ToArray(),
        };

        SetCostObservation(cohorts[0], SimulatedCostObservationMode.Unresolved);
        simulation.NotifyProgressionChanged();
        simulation.RunFrames(400, completionsPerFrame: 1);
        Assert.All(cohorts[0], candidate => Assert.Equal(0, Progress(candidate)));
        Assert.True(simulation.World.DistinctCandidatesSubmitted >= 100);

        var secondCohortBeforeOutage = cohorts[1].ToDictionary(
            candidate => candidate.Uuid,
            Progress,
            StringComparer.Ordinal);
        SetCostObservation(cohorts[0], SimulatedCostObservationMode.Normal);
        SetCostObservation(cohorts[1], SimulatedCostObservationMode.Unresolved);
        simulation.NotifyProgressionChanged();
        Assert.True(simulation.RunUntil(
            _ => cohorts[0].All(candidate => Progress(candidate) > 0),
            maximumFrames: 2_000,
            completionsPerFrame: 1));
        Assert.All(cohorts[0], candidate => Assert.True(Progress(candidate) > 0));
        Assert.All(cohorts[1], candidate =>
            Assert.Equal(secondCohortBeforeOutage[candidate.Uuid], Progress(candidate)));

        var thirdCohortBeforeOutage = cohorts[2].ToDictionary(
            candidate => candidate.Uuid,
            Progress,
            StringComparer.Ordinal);
        SetCostObservation(cohorts[1], SimulatedCostObservationMode.Normal);
        SetCostObservation(cohorts[2], SimulatedCostObservationMode.Unresolved);
        simulation.NotifyProgressionChanged();
        Assert.True(simulation.RunUntil(
            _ => cohorts[1].All(candidate =>
                Progress(candidate) > secondCohortBeforeOutage[candidate.Uuid]),
            maximumFrames: 2_000,
            completionsPerFrame: 1));
        Assert.All(cohorts[1], candidate =>
            Assert.True(Progress(candidate) > secondCohortBeforeOutage[candidate.Uuid]));
        Assert.All(cohorts[2], candidate =>
            Assert.Equal(thirdCohortBeforeOutage[candidate.Uuid], Progress(candidate)));

        SetCostObservation(cohorts[2], SimulatedCostObservationMode.Normal);
        simulation.NotifyProgressionChanged();
        Assert.True(simulation.RunUntil(
            _ => cohorts[2].All(candidate => Progress(candidate) > thirdCohortBeforeOutage[candidate.Uuid]),
            maximumFrames: 2_000,
            completionsPerFrame: 1));

        Assert.True(simulation.World.TotalCandidateEvaluations <= 70_000);
        AssertBoundedWorld(simulation);
    }

    [Fact]
    [Trait("Category", "AutoBuyReliability")]
    public void CompletionInvalidationStormWithBulkThree_CoalescesAndPreservesGroupFairness()
    {
        var specs = BuildStructures("storm", candidateCount: 130, maximumLevel: 1_000).ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            specs,
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Catalog.BulkDevelopment = 3;

        simulation.RunFrames(360);
        for (var frame = 0; frame < 1_200; frame++)
        {
            simulation.Step(completionsBeforeTick: 8);
        }

        Assert.Equal(simulation.World.TotalAutomationCompleted, simulation.Catalog.CompletionSignals);
        Assert.True(simulation.Catalog.EvaluationBatches < simulation.Catalog.CompletionSignals,
            "Completion callbacks were not coalesced into fewer evaluation batches.");
        Assert.Equal(specs.Length, simulation.World.DistinctCandidatesSubmitted);
        Assert.True(MaximumConsecutiveSubmissions(simulation.World.SubmissionOrder) <= 3);
        Assert.True(simulation.World.TotalCandidateEvaluations <= 70_000);
        AssertBoundedWorld(simulation);
    }

    [Fact]
    [Trait("Category", "AutoBuyReliability")]
    public void NearThresholdPartialBulkGroups_StopAndResumeAtExactLiveBoundaries()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 20,
            new[]
            {
                new SimulatedCandidateSpec(
                    "threshold-structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0,
                    costScaling: 2.0,
                    maximumLevel: 10),
            },
            initialResourceQuantity: 74.0);
        simulation.Config.AbsoluteReserve.Value = "5";
        simulation.Catalog.BulkDevelopment = 10;

        simulation.RunFrames(30);
        var candidate = Assert.Single(simulation.World.Candidates);
        Assert.Equal(2, Progress(candidate));
        Assert.Equal(44.0, simulation.World.ResourceQuantity, precision: 6);

        simulation.SetResourceQuantity("resource", new BigAmount(44.0, 0));
        simulation.RunFrames(30);
        Assert.Equal(2, Progress(candidate));

        simulation.SetResourceQuantity("resource", new BigAmount(45.0, 0));
        Assert.True(simulation.RunUntil(
            _ => Progress(candidate) == 3,
            maximumFrames: 30));
        Assert.Equal(5.0, simulation.World.ResourceQuantity, precision: 6);

        simulation.SetResourceQuantity("resource", new BigAmount(84.0, 0));
        simulation.RunFrames(30);
        Assert.Equal(3, Progress(candidate));

        simulation.SetResourceQuantity("resource", new BigAmount(85.0, 0));
        Assert.True(simulation.RunUntil(
            _ => Progress(candidate) == 4,
            maximumFrames: 30));
        Assert.Equal(5.0, simulation.World.ResourceQuantity, precision: 6);
        AssertBoundedWorld(simulation);
    }

    [Fact]
    public void HeavyTailCandidateEvaluation_ResumesAfterAnIndivisibleThirtyFiveMillisecondRead()
    {
        var specs = BuildStructures("heavy", candidateCount: 180, maximumLevel: 100).ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            specs,
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1,
            readObservationCostSchedule: observation =>
                observation == 20 ? 35.0 : 0.02);
        simulation.Catalog.BulkDevelopment = 3;

        simulation.RunFrames(1_000, completionsPerFrame: 1);

        Assert.InRange(simulation.Metrics.MaximumEvaluationsInFrame, 20, 21);
        Assert.Equal(specs.Length, simulation.World.DistinctCandidatesSubmitted);
        Assert.True(simulation.World.TotalSubmitted >= 700);
        Assert.True(simulation.World.TotalCandidateEvaluations <= 20_000);
        AssertBoundedWorld(simulation);
    }

    [Fact]
    [Trait("Category", "AutoBuyReliability")]
    public void CatalogRampUnderLoad_RegistersNewCandidatesWithoutStarvingExistingWork()
    {
        var nextIndex = 0;
        using var simulation = new AutoBuySimulation(
            queueCapacity: 96,
            BuildStructures("ramp", candidateCount: 28, maximumLevel: 1_000, startIndex: nextIndex),
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);
        nextIndex += 28;
        simulation.Catalog.BulkDevelopment = 3;
        simulation.RunFrames(180, completionsPerFrame: 1);

        foreach (var batchSize in new[] { 27, 27, 27, 28 })
        {
            var previousCandidates = simulation.World.Candidates.ToArray();
            simulation.RegisterCandidates(BuildStructures(
                "ramp",
                batchSize,
                maximumLevel: 1_000,
                startIndex: nextIndex));
            nextIndex += batchSize;
            simulation.RunFrames(240, completionsPerFrame: 1);
            Assert.All(previousCandidates, candidate => Assert.True(Progress(candidate) > 0));
        }

        Assert.Equal(RuntimeCatalogSize, simulation.World.Candidates.Count);
        Assert.True(simulation.RunUntil(
            world => world.DistinctCandidatesSubmitted == RuntimeCatalogSize,
            maximumFrames: 800,
            completionsPerFrame: 1));
        Assert.All(simulation.World.Candidates, candidate => Assert.True(Progress(candidate) > 0));
        Assert.True(simulation.World.TotalCandidateEvaluations <= 35_000);
        AssertBoundedWorld(simulation);
    }

    private static AutoBuySimulation CreateRuntimeCatalogSimulation(int maximumLevel)
    {
        var simulation = new AutoBuySimulation(
            queueCapacity: 192,
            BuildStructures("outage", RuntimeCatalogSize, maximumLevel),
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Catalog.BulkDevelopment = 3;
        return simulation;
    }

    private static IEnumerable<SimulatedCandidateSpec> BuildEndgameCandidates()
    {
        foreach (var spec in BuildStructures(
                     "low-bulk-endgame",
                     EndgameStructureCount,
                     maximumLevel: 1_000))
        {
            yield return spec;
        }

        for (var index = 0; index < EndgameUpgradeCount; index++)
        {
            yield return new SimulatedCandidateSpec(
                $"low-bulk-endgame-upgrade-{index:000}",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 25.0 + (index % 13),
                maximumLevel: 1);
        }
    }

    private static IEnumerable<SimulatedCandidateSpec> BuildStructures(
        string prefix,
        int candidateCount,
        int maximumLevel,
        int startIndex = 0)
    {
        for (var offset = 0; offset < candidateCount; offset++)
        {
            var index = startIndex + offset;
            yield return new SimulatedCandidateSpec(
                $"{prefix}-structure-{index:000}",
                AutoBuyCandidateKind.Structure,
                baseCost: 1.0 + (index % 11),
                costScaling: 1.001,
                maximumLevel: maximumLevel);
        }
    }

    private static void SetCostObservation(
        IEnumerable<SimulatedAutoBuyCandidate> candidates,
        SimulatedCostObservationMode mode)
    {
        foreach (var candidate in candidates)
        {
            candidate.CostObservationMode = mode;
        }
    }

    private static int Progress(SimulatedAutoBuyCandidate candidate) =>
        candidate.CurrentLevel + candidate.QueuedLevels;

    private static int MaximumConsecutiveSubmissions(IReadOnlyList<string> submissionOrder)
    {
        var maximum = 0;
        var current = 0;
        string? previous = null;
        foreach (var uuid in submissionOrder)
        {
            current = string.Equals(previous, uuid, StringComparison.Ordinal)
                ? current + 1
                : 1;
            maximum = Math.Max(maximum, current);
            previous = uuid;
        }

        return maximum;
    }

    private static void AssertBoundedWorld(AutoBuySimulation simulation)
    {
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
        Assert.True(simulation.World.TotalAutomationCompleted <= simulation.World.TotalSubmitted);
        Assert.True(simulation.Metrics.MaximumPurchasesInFrame <= 1);
        Assert.True(simulation.Metrics.MaximumEvaluationsInFrame <= 55);
    }
}
