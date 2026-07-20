using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;
using Xunit;
using Xunit.Sdk;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuySimulationStateMachineTests
{
    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData(1701)]
    [InlineData(2718)]
    [InlineData(31415)]
    [InlineData(65537)]
    public void SeededEventTape_PreservesInvariantsAndReplaysDeterministically(int seed)
    {
        var tape = BuildTape(seed, eventCount: 240);
        StateMachineOutcome first;
        try
        {
            first = Execute(seed, tape, tape.Count);
        }
        catch (Exception exception)
        {
            throw new XunitException(BuildReducedFailure(seed, tape, exception));
        }

        var second = Execute(seed, tape, tape.Count);

        Assert.Equal(first.SubmissionOrder, second.SubmissionOrder);
        Assert.Equal(first.TotalSubmitted, second.TotalSubmitted);
        Assert.Equal(first.AutomationCompleted, second.AutomationCompleted);
        Assert.Equal(first.ManualCompleted, second.ManualCompleted);
        Assert.Equal(first.CandidateEvaluations, second.CandidateEvaluations);
        Assert.Equal(first.PurchaseCalls, second.PurchaseCalls);
        Assert.Equal(first.QueueCount, second.QueueCount);
        Assert.Equal(first.ResourceQuantity, second.ResourceQuantity, 6);
    }

    private static StateMachineOutcome Execute(
        int seed,
        IReadOnlyList<SimulationEvent> tape,
        int eventCount)
    {
        using var simulation = CreateSimulation();
        var emergencyDisabled = false;
        for (var index = 0; index < eventCount; index++)
        {
            var item = tape[index];
            var submittedBefore = simulation.World.TotalSubmitted;
            var completions = Apply(simulation, item, ref emergencyDisabled);
            simulation.Step(completionsBeforeTick: completions);
            AssertInvariants(simulation, seed, index, item, submittedBefore, emergencyDisabled);
        }

        return new StateMachineOutcome(
            simulation.World.SubmissionOrder.ToArray(),
            simulation.World.TotalSubmitted,
            simulation.World.TotalAutomationCompleted,
            simulation.World.TotalManualCompleted,
            simulation.World.TotalCandidateEvaluations,
            simulation.World.TotalPurchaseCalls,
            simulation.World.QueueCount,
            simulation.World.ResourceQuantity);
    }

    private static int Apply(
        AutoBuySimulation simulation,
        SimulationEvent item,
        ref bool emergencyDisabled)
    {
        switch (item.Kind)
        {
            case SimulationEventKind.Resource:
                simulation.SetResourceQuantity("resource", new BigAmount(item.Value, 0));
                break;
            case SimulationEventKind.Availability:
                simulation.World.Candidates[item.CandidateIndex].Available = item.Enabled;
                simulation.NotifyProgressionChanged();
                break;
            case SimulationEventKind.ManualEnqueue:
                simulation.World.TryEnqueueManualActions(item.Count, out _);
                break;
            case SimulationEventKind.Completion:
                return item.Count;
            case SimulationEventKind.Capacity:
                simulation.World.SetQueueCapacity(
                    Math.Max(simulation.World.QueueCount, item.Count));
                break;
            case SimulationEventKind.QueueObservation:
                simulation.Catalog.QueueReadSucceeds = item.Enabled;
                break;
            case SimulationEventKind.CostObservation:
                simulation.World.Candidates[item.CandidateIndex].CostObservationMode =
                    item.Enabled
                        ? SimulatedCostObservationMode.Unresolved
                        : SimulatedCostObservationMode.Normal;
                simulation.NotifyProgressionChanged();
                break;
            case SimulationEventKind.PurchaseFault:
                simulation.World.Candidates[item.CandidateIndex].FailureMode =
                    item.Enabled
                        ? SimulatedPurchaseFailureMode.RejectBeforeMutation
                        : SimulatedPurchaseFailureMode.None;
                break;
            case SimulationEventKind.Reserve:
                simulation.Config.AbsoluteReserve.Value =
                    item.Value.ToString(CultureInfo.InvariantCulture);
                break;
            case SimulationEventKind.Lifecycle:
                simulation.ReloadLifecycle(
                    clearQueue: true,
                    replaceNativeIdentities: item.Enabled,
                    replaceCandidateWrappers: !item.Enabled);
                break;
            case SimulationEventKind.Emergency:
                emergencyDisabled = item.Enabled;
                simulation.SetEmergencyDisabled(emergencyDisabled);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return 0;
    }

    private static void AssertInvariants(
        AutoBuySimulation simulation,
        int seed,
        int eventIndex,
        SimulationEvent item,
        int submittedBefore,
        bool emergencyDisabled)
    {
        var prefix = $"seed={seed}, event={eventIndex}, action={item.ToReplayLine()}";
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity,
            $"Queue overflow: {prefix}");
        Assert.True(simulation.World.ResourceQuantity >= 0.0,
            $"Negative resource: {prefix}");
        Assert.True(simulation.World.TotalAutomationCompleted <= simulation.World.TotalSubmitted,
            $"Completion exceeded submissions: {prefix}");
        Assert.True(simulation.Metrics.MaximumPurchasesInFrame <= 1,
            $"More than one mutation was admitted in a frame: {prefix}");
        if (emergencyDisabled)
        {
            Assert.Equal(submittedBefore, simulation.World.TotalSubmitted);
        }

        foreach (var candidate in simulation.World.Candidates)
        {
            Assert.True(candidate.CurrentLevel >= 0 && candidate.QueuedLevels >= 0,
                $"Negative level state for {candidate.Uuid}: {prefix}");
            if (candidate.MaximumLevel.HasValue)
            {
                Assert.True(candidate.CurrentLevel + candidate.QueuedLevels <= candidate.MaximumLevel.Value,
                    $"Finite maximum exceeded for {candidate.Uuid}: {prefix}");
            }
        }
    }

    private static AutoBuySimulation CreateSimulation()
    {
        var specs = Enumerable.Range(0, 8)
            .Select(index => new SimulatedCandidateSpec(
                $"state-{index:00}",
                index % 4 == 0 ? AutoBuyCandidateKind.Upgrade : AutoBuyCandidateKind.Structure,
                baseCost: 2.0 + index,
                costScaling: 1.01,
                maximumLevel: index % 4 == 0 ? 1 : 50))
            .ToArray();
        var simulation = new AutoBuySimulation(16, specs, initialResourceQuantity: 500.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        return simulation;
    }

    private static IReadOnlyList<SimulationEvent> BuildTape(int seed, int eventCount)
    {
        var random = new Random(seed);
        var events = new List<SimulationEvent>(eventCount);
        for (var index = 0; index < eventCount; index++)
        {
            var kind = (SimulationEventKind)random.Next(0, 11);
            events.Add(kind switch
            {
                SimulationEventKind.Resource => new(kind, Value: random.Next(0, 601)),
                SimulationEventKind.Availability => new(kind, random.Next(0, 8), Enabled: random.Next(0, 2) == 1),
                SimulationEventKind.ManualEnqueue => new(kind, Count: random.Next(1, 4)),
                SimulationEventKind.Completion => new(kind, Count: random.Next(1, 4)),
                SimulationEventKind.Capacity => new(kind, Count: new[] { 4, 8, 16, 24 }[random.Next(0, 4)]),
                SimulationEventKind.QueueObservation => new(kind, Enabled: random.Next(0, 3) != 0),
                SimulationEventKind.CostObservation => new(kind, random.Next(0, 8), Enabled: random.Next(0, 4) == 0),
                SimulationEventKind.PurchaseFault => new(kind, random.Next(0, 8), Enabled: random.Next(0, 4) == 0),
                SimulationEventKind.Reserve => new(kind, Value: new[] { 0, 20, 50, 100 }[random.Next(0, 4)]),
                SimulationEventKind.Lifecycle => new(kind, Enabled: random.Next(0, 2) == 1),
                SimulationEventKind.Emergency => new(kind, Enabled: random.Next(0, 3) == 0),
                _ => throw new ArgumentOutOfRangeException(),
            });
        }

        return events;
    }

    private static string BuildReducedFailure(
        int seed,
        IReadOnlyList<SimulationEvent> tape,
        Exception original)
    {
        var firstFailingPrefix = tape.Count;
        for (var prefix = 1; prefix <= tape.Count; prefix++)
        {
            try
            {
                Execute(seed, tape, prefix);
            }
            catch
            {
                firstFailingPrefix = prefix;
                break;
            }
        }

        var start = Math.Max(0, firstFailingPrefix - 12);
        var builder = new StringBuilder();
        builder.AppendLine(original.Message);
        builder.AppendLine($"Seed: {seed}; minimal failing prefix: {firstFailingPrefix}/{tape.Count}");
        builder.AppendLine("Replay-compatible/synthetic event tail:");
        for (var index = start; index < firstFailingPrefix; index++)
        {
            builder.AppendLine($"  {index:000}: {tape[index].ToReplayLine()}");
        }

        return builder.ToString();
    }

    private enum SimulationEventKind
    {
        Resource,
        Availability,
        ManualEnqueue,
        Completion,
        Capacity,
        QueueObservation,
        CostObservation,
        PurchaseFault,
        Reserve,
        Lifecycle,
        Emergency,
    }

    private readonly record struct SimulationEvent(
        SimulationEventKind Kind,
        int CandidateIndex = 0,
        int Count = 0,
        double Value = 0.0,
        bool Enabled = false)
    {
        public string ToReplayLine() => Kind switch
        {
            SimulationEventKind.Resource => $"resource resource {Value.ToString(CultureInfo.InvariantCulture)}",
            SimulationEventKind.Availability => $"progression state-{CandidateIndex:00} available={Enabled}",
            SimulationEventKind.ManualEnqueue => $"queue manual +{Count}",
            SimulationEventKind.Completion => $"completion front {Count}",
            SimulationEventKind.Capacity => $"queue capacity {Count}",
            SimulationEventKind.Lifecycle => $"lifecycle reload wrappers={!Enabled}",
            SimulationEventKind.Reserve => $"config reserve {Value.ToString(CultureInfo.InvariantCulture)}",
            SimulationEventKind.Emergency => $"config emergency {Enabled}",
            _ => $"synthetic {Kind} candidate={CandidateIndex} enabled={Enabled}",
        };
    }

    private readonly record struct StateMachineOutcome(
        string[] SubmissionOrder,
        int TotalSubmitted,
        int AutomationCompleted,
        int ManualCompleted,
        int CandidateEvaluations,
        int PurchaseCalls,
        int QueueCount,
        double ResourceQuantity);
}
