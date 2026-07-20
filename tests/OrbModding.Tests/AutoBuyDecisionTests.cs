using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyDecisionTests
{
    [Theory]
    [Trait("Category", "AutoBuyDecision")]
    [InlineData((int)AutoBuyStructureRepeatMode.Single, 1)]
    [InlineData((int)AutoBuyStructureRepeatMode.Fixed, 2)]
    [InlineData((int)AutoBuyStructureRepeatMode.BulkDevelopment, 3)]
    public void CurrentFallbackContinuation_ReevaluatesAfterEachStructureGroup(
        int repeatModeValue,
        int structureGroupSize)
    {
        var repeatMode = (AutoBuyStructureRepeatMode)repeatModeValue;
        var specs = new[]
        {
            new SimulatedCandidateSpec("structure-a", AutoBuyCandidateKind.Structure, baseCost: 1.0),
            new SimulatedCandidateSpec("structure-b", AutoBuyCandidateKind.Structure, baseCost: 2.0),
            new SimulatedCandidateSpec(
                "upgrade-a",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 3.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(16, specs);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.RepeatWhileAffordable.Value = false;
        simulation.Config.StructureRepeatMode.Value = repeatMode;
        simulation.Config.FixedStructureLevelsPerCandidate.Value = structureGroupSize;
        simulation.Catalog.BulkDevelopment = structureGroupSize;

        var expected = Repeat("structure-a", structureGroupSize + 1).ToArray();

        Assert.True(simulation.RunUntil(
            world => world.TotalSubmitted >= expected.Length,
            maximumFrames: 100));
        Assert.Equal(expected, simulation.World.SubmissionOrder.Take(expected.Length));
    }

    [Fact]
    [Trait("Category", "AutoBuyDecision")]
    public void CurrentPrecedence_RepeatWhileAffordableUsesOneCandidateLevelPerRankedPass()
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec("structure-a", AutoBuyCandidateKind.Structure, baseCost: 1.0),
            new SimulatedCandidateSpec("structure-b", AutoBuyCandidateKind.Structure, baseCost: 2.0),
            new SimulatedCandidateSpec(
                "upgrade-a",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 3.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(16, specs);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.RepeatWhileAffordable.Value = true;
        simulation.Config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.BulkDevelopment;
        simulation.Catalog.BulkDevelopment = 4;

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted >= 3, maximumFrames: 100));

        Assert.Equal(
            new[] { "structure-a", "structure-b", "upgrade-a" },
            simulation.World.SubmissionOrder.Take(3));
        Assert.Equal(0, simulation.Catalog.BulkDevelopmentReads);
    }

    [Fact]
    [Trait("Category", "AutoBuyDecision")]
    public void RepeatWhileAffordable_VisitsEveryRankedCandidateBeforeRepeating()
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec("structure-a", AutoBuyCandidateKind.Structure, baseCost: 1.0),
            new SimulatedCandidateSpec("structure-b", AutoBuyCandidateKind.Structure, baseCost: 2.0),
            new SimulatedCandidateSpec("structure-c", AutoBuyCandidateKind.Structure, baseCost: 3.0),
            new SimulatedCandidateSpec(
                "upgrade-a",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 4.0,
                maximumLevel: 1),
            new SimulatedCandidateSpec(
                "upgrade-b",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 5.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(16, specs);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.RepeatWhileAffordable.Value = true;

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted >= specs.Length, maximumFrames: 100));
        Assert.Equal(
            specs.Select(spec => spec.Uuid),
            simulation.World.SubmissionOrder.Take(specs.Length));
    }

    [Fact]
    [Trait("Category", "AutoBuyDecision")]
    public void UnavailableCandidate_DoesNotChangeEligibleSubmissionOrder()
    {
        var baseline = RunEligiblePrefix(includeUnavailableCandidate: false);
        var withUnavailable = RunEligiblePrefix(includeUnavailableCandidate: true);

        Assert.Equal(baseline, withUnavailable);
    }

    [Fact]
    [Trait("Category", "AutoBuyDecision")]
    public void StrongerAbsoluteReserve_CannotIncreaseAcceptedPurchases()
    {
        var withoutReserve = CountAcceptedPurchases("0");
        var withReserve = CountAcceptedPurchases("50");

        Assert.True(withReserve <= withoutReserve);
        Assert.Equal(10, withoutReserve);
        Assert.Equal(5, withReserve);
    }

    [Fact]
    [Trait("Category", "AutoBuyDecision")]
    public void IdenticalWorlds_ProduceIdenticalOrdersAndWorkCounts()
    {
        var first = RunDeterministicWorld();
        var second = RunDeterministicWorld();

        Assert.Equal(first.SubmissionOrder, second.SubmissionOrder);
        Assert.Equal(first.TotalSubmitted, second.TotalSubmitted);
        Assert.Equal(first.CandidateEvaluations, second.CandidateEvaluations);
        Assert.Equal(first.IdleFrames, second.IdleFrames);
    }

    private static string[] RunEligiblePrefix(bool includeUnavailableCandidate)
    {
        var specs = new List<SimulatedCandidateSpec>();
        if (includeUnavailableCandidate)
        {
            specs.Add(new SimulatedCandidateSpec(
                "unavailable",
                AutoBuyCandidateKind.Structure,
                baseCost: 0.01,
                available: false));
        }

        specs.Add(new SimulatedCandidateSpec("structure-a", AutoBuyCandidateKind.Structure, baseCost: 1.0));
        specs.Add(new SimulatedCandidateSpec(
            "upgrade-a",
            AutoBuyCandidateKind.Upgrade,
            baseCost: 2.0,
            maximumLevel: 1));

        using var simulation = new AutoBuySimulation(8, specs);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.RepeatWhileAffordable.Value = false;
        simulation.Config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Single;
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted >= 2, maximumFrames: 100));
        return simulation.World.SubmissionOrder.Take(2).ToArray();
    }

    private static int CountAcceptedPurchases(string absoluteReserve)
    {
        using var simulation = new AutoBuySimulation(
            20,
            new[]
            {
                new SimulatedCandidateSpec(
                    "structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0),
            },
            initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.AbsoluteReserve.Value = absoluteReserve;
        simulation.Config.RepeatWhileAffordable.Value = true;
        simulation.RunFrames(100);
        return simulation.World.TotalSubmitted;
    }

    private static DeterministicOutcome RunDeterministicWorld()
    {
        var specs = Enumerable.Range(0, 12)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:00}",
                index % 3 == 0
                    ? AutoBuyCandidateKind.Upgrade
                    : AutoBuyCandidateKind.Structure,
                baseCost: 1.0 + index,
                maximumLevel: index % 3 == 0 ? 1 : null))
            .ToArray();
        using var simulation = new AutoBuySimulation(24, specs);
        simulation.Config.LeaveQueueSlots.Value = 1;
        for (var frame = 0; frame < 200; frame++)
        {
            simulation.Step(frame >= 40 && frame % 7 == 0 ? 1 : 0);
        }

        return new DeterministicOutcome(
            simulation.World.SubmissionOrder.ToArray(),
            simulation.World.TotalSubmitted,
            simulation.World.TotalCandidateEvaluations,
            simulation.Metrics.IdleFramesWithPurchasableWork);
    }

    private static IEnumerable<string> Repeat(string value, int count) =>
        Enumerable.Repeat(value, count);

    private sealed class DeterministicOutcome
    {
        public DeterministicOutcome(
            string[] submissionOrder,
            int totalSubmitted,
            int candidateEvaluations,
            int idleFrames)
        {
            SubmissionOrder = submissionOrder;
            TotalSubmitted = totalSubmitted;
            CandidateEvaluations = candidateEvaluations;
            IdleFrames = idleFrames;
        }

        public string[] SubmissionOrder { get; }

        public int TotalSubmitted { get; }

        public int CandidateEvaluations { get; }

        public int IdleFrames { get; }
    }
}
