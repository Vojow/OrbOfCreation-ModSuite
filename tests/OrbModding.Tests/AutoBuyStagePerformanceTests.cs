using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyStagePerformanceTests
{
    // data/entity-mappings.tsv contains these definition counts in the reviewed
    // serialized snapshot. Stress profiles use subsets independently of live availability.
    private const int AllKnownStructureCount = 180;
    private const int AllKnownUpgradeCount = 223;

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    [Trait("Category", "AutoBuyPerformance")]
    public void EarlyStressProfile_FewCandidatesAndSlowCompletionsRemainResponsive() =>
        RunStage(new StageScenario(
            "stage-early",
            "early",
            structureCount: 8,
            upgradeCount: 2,
            targetStructureLevels: 10,
            bulkDevelopment: 10,
            queueCapacity: 24,
            frameCount: 5100,
            completionStartFrame: 120,
            completionEveryFrames: 60,
            minimumQueueFraction: 0.9,
            maximumIdleFrames: 4));

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    [Trait("Category", "AutoBuyPerformance")]
    public void MidStressProfile_MoreCandidatesAndFasterCompletionsRemainResponsive() =>
        RunStage(new StageScenario(
            "stage-mid",
            "mid",
            structureCount: 64,
            upgradeCount: 12,
            targetStructureLevels: 40,
            bulkDevelopment: 25,
            queueCapacity: 128,
            frameCount: 39000,
            completionStartFrame: 240,
            completionEveryFrames: 15,
            minimumQueueFraction: 0.9,
            maximumIdleFrames: 8));

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    [Trait("Category", "AutoBuyPerformance")]
    public void LateStressProfile_AllMappedStructuresAndPeriodicFastCompletionsRemainBounded()
    {
        AssertCanonicalCatalogCounts();
        RunStage(new StageScenario(
            "stage-late",
            "late",
            structureCount: AllKnownStructureCount,
            upgradeCount: 24,
            targetStructureLevels: 100,
            bulkDevelopment: 100,
            queueCapacity: 304,
            frameCount: 73000,
            completionStartFrame: 400,
            completionEveryFrames: 4,
            minimumQueueFraction: 0.9,
            maximumIdleFrames: 600));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    [Trait("Category", "AutoBuyPerformance")]
    public void EndgameStressProfile_SameCatalogAndPerFrameCompletionsRemainBounded()
    {
        AssertCanonicalCatalogCounts();
        RunStage(new StageScenario(
            "stage-endgame",
            "endgame",
            structureCount: AllKnownStructureCount,
            upgradeCount: 24,
            targetStructureLevels: 1000,
            bulkDevelopment: 100,
            queueCapacity: 304,
            frameCount: 200000,
            completionStartFrame: 400,
            completionEveryFrames: 1,
            minimumQueueFraction: 0.0,
            maximumIdleFrames: 8000));
    }

    private static void RunStage(StageScenario stage)
    {
        var candidates = BuildCandidates(stage).ToArray();
        using var simulation = new AutoBuySimulation(
            stage.QueueCapacity,
            candidates,
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);
        simulation.Catalog.BulkDevelopment = stage.BulkDevelopment;

        var requiredSubmissions = checked(
            (stage.StructureCount * stage.TargetStructureLevels) + stage.UpgradeCount);
        var theoreticalMinimumSubmissionFrames = CalculateTheoreticalMinimumSubmissionFrames(
            stage,
            requiredSubmissions,
            stage.QueueCapacity - simulation.Config.LeaveQueueSlots.Value);
        int? framesToAllSubmissions = null;
        int? framesToAllCompletions = null;
        for (var frame = 0; frame < stage.FrameCount; frame++)
        {
            var completions = frame >= stage.CompletionStartFrame &&
                              (frame - stage.CompletionStartFrame) % stage.CompletionEveryFrames == 0
                ? 1
                : 0;
            simulation.Step(completions);
            if (!framesToAllSubmissions.HasValue &&
                simulation.World.TotalSubmitted == requiredSubmissions)
            {
                framesToAllSubmissions = frame + 1;
            }

            if (!framesToAllCompletions.HasValue &&
                simulation.World.TotalCompleted == requiredSubmissions)
            {
                framesToAllCompletions = frame + 1;
            }
        }

        AutoBuyPerformanceReporter.Record(
            stage.Name,
            stage.GameStage,
            simulation,
            candidates.Length,
            stage.StructureCount,
            stage.UpgradeCount,
            stage.TargetStructureLevels,
            simulation.Config.PurchaseGrouping.Value.ToString(),
            stage.BulkDevelopment,
            stage.QueueCapacity,
            simulation.Config.LeaveQueueSlots.Value,
            stage.FrameCount,
            stage.CompletionStartFrame,
            stage.CompletionEveryFrames,
            framesToAllSubmissions,
            framesToAllCompletions,
            theoreticalMinimumSubmissionFrames);

        var structures = simulation.World.Candidates
            .Where(candidate => candidate.Kind == AutoBuyCandidateKind.Structure)
            .ToArray();
        var upgrades = simulation.World.Candidates
            .Where(candidate => candidate.Kind == AutoBuyCandidateKind.Upgrade)
            .ToArray();
        var evaluationBudget = (4 * candidates.Length) + (4 * simulation.World.TotalSubmitted);
        var usableQueueCapacity = stage.QueueCapacity - simulation.Config.LeaveQueueSlots.Value;
        var minimumHealthyQueueDepth =
            (int)Math.Floor(usableQueueCapacity * stage.MinimumQueueFraction);

        Assert.Equal(stage.StructureCount, structures.Length);
        Assert.Equal(stage.UpgradeCount, upgrades.Length);
        Assert.All(structures, structure =>
            Assert.Equal(
                stage.TargetStructureLevels,
                structure.CurrentLevel + structure.QueuedLevels));
        Assert.All(upgrades, upgrade =>
            Assert.Equal(1, upgrade.CurrentLevel + upgrade.QueuedLevels));
        Assert.Equal(candidates.Length, simulation.World.DistinctCandidatesSubmitted);
        Assert.NotNull(framesToAllSubmissions);
        Assert.NotNull(framesToAllCompletions);
        Assert.True(framesToAllSubmissions <= stage.FrameCount,
            $"{stage.GameStage} did not submit every purchase target within " +
            $"{stage.FrameCount} frames.");
        Assert.True(framesToAllCompletions <= stage.FrameCount,
            $"{stage.GameStage} did not complete every purchase target within " +
            $"{stage.FrameCount} frames.");
        Assert.True(framesToAllSubmissions >= theoreticalMinimumSubmissionFrames,
            $"{stage.GameStage} beat the one-mutation-per-frame theoretical minimum.");
        Assert.Equal(
            simulation.Metrics.IdleFramesWithPurchasableWork,
            simulation.Metrics.EvaluationOnlyFramesWithPurchasableWork +
            simulation.Metrics.DeferredFramesWithPurchasableWork);
        Assert.Equal(usableQueueCapacity, simulation.World.QueueHighWater);
        Assert.True(simulation.World.QueueCount <= stage.QueueCapacity);
        Assert.Equal(0, simulation.World.QueueCount);
        Assert.Equal(requiredSubmissions, simulation.World.TotalCompleted);
        Assert.True(simulation.Metrics.MinimumQueueAfterSaturation >= minimumHealthyQueueDepth,
            $"{stage.GameStage} queue dropped below its " +
            $"{stage.MinimumQueueFraction:P0} post-saturation floor.");
        Assert.True(simulation.Metrics.FramesToNinetyPercentQueue <= stage.CompletionStartFrame,
            $"{stage.GameStage} did not saturate the queue before completion turnover began.");
        Assert.True(simulation.World.TotalCandidateEvaluations <= evaluationBudget,
            $"{stage.GameStage} evaluated {simulation.World.TotalCandidateEvaluations} candidates; " +
            $"budget was {evaluationBudget}.");
        Assert.True(simulation.Metrics.MaximumEvaluationsInFrame <= 55,
            $"{stage.GameStage} evaluated {simulation.Metrics.MaximumEvaluationsInFrame} candidates in one frame.");
        Assert.True(simulation.Metrics.IdleFramesWithPurchasableWork <= stage.MaximumIdleFrames,
            $"{stage.GameStage} left purchasable queue room idle for " +
            $"{simulation.Metrics.IdleFramesWithPurchasableWork} frames.");
    }

    private static IEnumerable<SimulatedCandidateSpec> BuildCandidates(StageScenario stage)
    {
        for (var index = 0; index < stage.StructureCount; index++)
        {
            yield return new SimulatedCandidateSpec(
                $"{stage.GameStage}-structure-{index:000}",
                AutoBuyCandidateKind.Structure,
                baseCost: 1.0 + (index % 11),
                costScaling: 1.01,
                maximumLevel: stage.TargetStructureLevels);
        }

        for (var index = 0; index < stage.UpgradeCount; index++)
        {
            yield return new SimulatedCandidateSpec(
                $"{stage.GameStage}-upgrade-{index:000}",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 25.0 + (index % 13),
                maximumLevel: 1);
        }
    }

    private static void AssertCanonicalCatalogCounts()
    {
        var mappingPath = Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv");
        var mappings = File.ReadLines(mappingPath).Skip(1).ToArray();
        var mappedStructures = mappings.Count(line =>
            line.EndsWith("\tStructureSO", StringComparison.Ordinal));
        var mappedUpgrades = mappings.Count(line =>
            line.EndsWith("\tUpgradeSO", StringComparison.Ordinal));
        Assert.Equal(AllKnownStructureCount, mappedStructures);
        Assert.Equal(AllKnownUpgradeCount, mappedUpgrades);
    }

    private static int CalculateTheoreticalMinimumSubmissionFrames(
        StageScenario stage,
        int requiredSubmissions,
        int usableQueueCapacity)
    {
        var queueDepth = 0;
        var submitted = 0;
        for (var frame = 0; frame < stage.FrameCount; frame++)
        {
            if (frame >= stage.CompletionStartFrame &&
                (frame - stage.CompletionStartFrame) % stage.CompletionEveryFrames == 0 &&
                queueDepth > 0)
            {
                queueDepth--;
            }

            if (submitted < requiredSubmissions && queueDepth < usableQueueCapacity)
            {
                submitted++;
                queueDepth++;
            }

            if (submitted == requiredSubmissions)
            {
                return frame + 1;
            }
        }

        throw new InvalidOperationException(
            $"The {stage.GameStage} theoretical scheduler cannot reach its target within the frame budget.");
    }

    private sealed class StageScenario
    {
        public StageScenario(
            string name,
            string gameStage,
            int structureCount,
            int upgradeCount,
            int targetStructureLevels,
            int bulkDevelopment,
            int queueCapacity,
            int frameCount,
            int completionStartFrame,
            int completionEveryFrames,
            double minimumQueueFraction,
            int maximumIdleFrames)
        {
            Name = name;
            GameStage = gameStage;
            StructureCount = structureCount;
            UpgradeCount = upgradeCount;
            TargetStructureLevels = targetStructureLevels;
            BulkDevelopment = bulkDevelopment;
            QueueCapacity = queueCapacity;
            FrameCount = frameCount;
            CompletionStartFrame = completionStartFrame;
            CompletionEveryFrames = completionEveryFrames;
            MinimumQueueFraction = minimumQueueFraction;
            MaximumIdleFrames = maximumIdleFrames;
        }

        public string Name { get; }

        public string GameStage { get; }

        public int StructureCount { get; }

        public int UpgradeCount { get; }

        public int TargetStructureLevels { get; }

        public int BulkDevelopment { get; }

        public int QueueCapacity { get; }

        public int FrameCount { get; }

        public int CompletionStartFrame { get; }

        public int CompletionEveryFrames { get; }

        public double MinimumQueueFraction { get; }

        public int MaximumIdleFrames { get; }
    }
}
