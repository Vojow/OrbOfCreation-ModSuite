using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrbModding.Tests.Simulation;

internal static class AutoBuyPerformanceReporter
{
    private const string ReportPathEnvironmentVariable = "OOC_PERFORMANCE_REPORT";
    private static readonly object Sync = new object();
    private static readonly SortedDictionary<string, PerformanceScenarioReport> Scenarios =
        new SortedDictionary<string, PerformanceScenarioReport>(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void Record(
        string name,
        AutoBuySimulation simulation,
        int candidateCount,
        int queueCapacity,
        int reservedQueueSlots,
        int frameCount,
        int completionStartFrame,
        int completionEveryFrames)
    {
        var reportPath = Environment.GetEnvironmentVariable(ReportPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        var world = simulation.World;
        var catalog = simulation.Catalog;
        var metrics = simulation.Metrics;
        var nativeReadOperations =
            world.TotalCandidateEvaluations +
            world.TotalCostReads +
            world.TotalLifecycleReads +
            catalog.QueueCapacityReads +
            catalog.BulkDevelopmentReads +
            catalog.ActionMultiplierReads;
        var nativeMutationAttempts = world.TotalPurchaseCalls;
        var schedulerCallbacks = catalog.EvaluationBatches + catalog.CompletedCandidateEvaluations;
        var totalObservedOperations = nativeReadOperations + nativeMutationAttempts + schedulerCallbacks;

        var scenario = new PerformanceScenarioReport
        {
            Name = name,
            Workload = new PerformanceWorkload
            {
                CandidateCount = candidateCount,
                QueueCapacity = queueCapacity,
                ReservedQueueSlots = reservedQueueSlots,
                FrameCount = frameCount,
                CompletionStartFrame = completionStartFrame,
                CompletionEveryFrames = completionEveryFrames,
            },
            Metrics = new PerformanceMetrics
            {
                TotalSubmitted = world.TotalSubmitted,
                QueueHighWater = world.QueueHighWater,
                FinalQueueDepth = world.QueueCount,
                MinimumQueueAfterSaturation = metrics.MinimumQueueAfterSaturation == int.MaxValue
                    ? null
                    : metrics.MinimumQueueAfterSaturation,
                FramesToNinetyPercentQueue = metrics.FramesToNinetyPercentQueue,
                IdleFramesWithPurchasableWork = metrics.IdleFramesWithPurchasableWork,
                MaximumEvaluationsInFrame = metrics.MaximumEvaluationsInFrame,
                DistinctCandidatesSubmitted = world.DistinctCandidatesSubmitted,
                TotalCandidateEvaluations = world.TotalCandidateEvaluations,
                TotalCostReads = world.TotalCostReads,
                TotalLifecycleReads = world.TotalLifecycleReads,
                TotalPurchaseCalls = world.TotalPurchaseCalls,
                QueueCapacityReads = catalog.QueueCapacityReads,
                BulkDevelopmentReads = catalog.BulkDevelopmentReads,
                ActionMultiplierReads = catalog.ActionMultiplierReads,
                EvaluationBatches = catalog.EvaluationBatches,
                CompletedCandidateEvaluations = catalog.CompletedCandidateEvaluations,
                CompletionSignals = catalog.CompletionSignals,
                NativeReadOperations = nativeReadOperations,
                NativeMutationAttempts = nativeMutationAttempts,
                SchedulerCallbacks = schedulerCallbacks,
                TotalObservedOperations = totalObservedOperations,
                CandidateEvaluationsPerPurchase = Divide(world.TotalCandidateEvaluations, world.TotalSubmitted),
                ObservedOperationsPerPurchase = Divide(totalObservedOperations, world.TotalSubmitted),
            },
        };

        lock (Sync)
        {
            Scenarios[name] = scenario;
            WriteReport(Path.GetFullPath(reportPath));
        }
    }

    private static double Divide(int numerator, int denominator) =>
        denominator == 0 ? 0.0 : Math.Round((double)numerator / denominator, 6);

    private static void WriteReport(string reportPath)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new PerformanceSuiteReport
        {
            SchemaVersion = 1,
            Suite = "OrbAutomata.AutoBuy",
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            SourceCommit =
                Environment.GetEnvironmentVariable("OOC_PERFORMANCE_SOURCE") ??
                Environment.GetEnvironmentVariable("GITHUB_SHA") ??
                "working-tree",
            Scenarios = Scenarios.Values.ToArray(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine);
    }
}

internal sealed class PerformanceSuiteReport
{
    public int SchemaVersion { get; init; }

    public string Suite { get; init; } = string.Empty;

    public string GeneratedAtUtc { get; init; } = string.Empty;

    public string SourceCommit { get; init; } = string.Empty;

    public IReadOnlyList<PerformanceScenarioReport> Scenarios { get; init; } =
        Array.Empty<PerformanceScenarioReport>();
}

internal sealed class PerformanceScenarioReport
{
    public string Name { get; init; } = string.Empty;

    public PerformanceWorkload Workload { get; init; } = new PerformanceWorkload();

    public PerformanceMetrics Metrics { get; init; } = new PerformanceMetrics();
}

internal sealed class PerformanceWorkload
{
    public int CandidateCount { get; init; }

    public int QueueCapacity { get; init; }

    public int ReservedQueueSlots { get; init; }

    public int FrameCount { get; init; }

    public int CompletionStartFrame { get; init; }

    public int CompletionEveryFrames { get; init; }
}

internal sealed class PerformanceMetrics
{
    public int TotalSubmitted { get; init; }

    public int QueueHighWater { get; init; }

    public int FinalQueueDepth { get; init; }

    public int? MinimumQueueAfterSaturation { get; init; }

    public int? FramesToNinetyPercentQueue { get; init; }

    public int IdleFramesWithPurchasableWork { get; init; }

    public int MaximumEvaluationsInFrame { get; init; }

    public int DistinctCandidatesSubmitted { get; init; }

    public int TotalCandidateEvaluations { get; init; }

    public int TotalCostReads { get; init; }

    public int TotalLifecycleReads { get; init; }

    public int TotalPurchaseCalls { get; init; }

    public int QueueCapacityReads { get; init; }

    public int BulkDevelopmentReads { get; init; }

    public int ActionMultiplierReads { get; init; }

    public int EvaluationBatches { get; init; }

    public int CompletedCandidateEvaluations { get; init; }

    public int CompletionSignals { get; init; }

    public int NativeReadOperations { get; init; }

    public int NativeMutationAttempts { get; init; }

    public int SchedulerCallbacks { get; init; }

    public int TotalObservedOperations { get; init; }

    public double CandidateEvaluationsPerPurchase { get; init; }

    public double ObservedOperationsPerPurchase { get; init; }
}
