using System;
using System.IO;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;

namespace OrbModding.AutoBuyComparison;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var reportPath = ReadRequiredArgument(args, "--report");
            var source = ReadRequiredArgument(args, "--source");
            Environment.SetEnvironmentVariable("OOC_PERFORMANCE_REPORT", Path.GetFullPath(reportPath));
            Environment.SetEnvironmentVariable("OOC_PERFORMANCE_SOURCE", source);

            RunScenario("periodic-completions", completionEveryFrames: 20);
            RunScenario("completion-storm", completionEveryFrames: 4);

            Console.WriteLine($"Wrote deterministic Auto Buy report for {source} to {Path.GetFullPath(reportPath)}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RunScenario(string name, int completionEveryFrames)
    {
        const int candidateCount = 166;
        const int queueCapacity = 304;
        const int frameCount = 900;
        const int completionStartFrame = 400;

        var candidates = Enumerable.Range(0, candidateCount)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + (index % 7)))
            .ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity,
            candidates,
            initialResourceQuantity: 1_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var completions = frame >= completionStartFrame &&
                              (frame - completionStartFrame) % completionEveryFrames == 0
                ? 1
                : 0;
            simulation.Step(completions);
        }

        AutoBuyPerformanceReporter.Record(
            name,
            simulation,
            candidateCount,
            queueCapacity,
            simulation.Config.LeaveQueueSlots.Value,
            frameCount,
            completionStartFrame,
            completionEveryFrames);

        if (simulation.World.QueueCount < 0 || simulation.World.QueueCount > queueCapacity)
        {
            throw new InvalidOperationException(
                $"Scenario {name} produced invalid queue depth {simulation.World.QueueCount}/{queueCapacity}.");
        }

        Console.WriteLine(
            $"{name}: submitted={simulation.World.TotalSubmitted}, " +
            $"queue={simulation.World.QueueCount}/{queueCapacity}, " +
            $"evaluations={simulation.World.TotalCandidateEvaluations}, " +
            $"idleFrames={simulation.Metrics.IdleFramesWithPurchasableWork}");
    }

    private static string ReadRequiredArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"Required argument is missing: {name}");
    }
}
