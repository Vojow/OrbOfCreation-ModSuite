using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuySimulationRaceTests
{
    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CapacityShrinkDuringPreparedGroup_StopsAtRefreshedLiveRoom()
    {
        using var simulation = CreateGroupedStructureSimulation(queueCapacity: 8);
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 1, maximumFrames: 20));

        simulation.World.SetQueueCapacity(1);
        simulation.RunFrames(20);

        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.QueueCount);

        Assert.Equal(1, simulation.World.Complete(1));
        simulation.World.SetQueueCapacity(4);
        simulation.NotifyProgressionChanged();

        Assert.True(simulation.RunUntil(world => world.QueueCount == 4, maximumFrames: 20));
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ManualActionTakingLastSlotImmediatelyBeforeSubmit_WinsAtomically()
    {
        using var simulation = new AutoBuySimulation(
            1,
            new[] { new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure) });
        simulation.Config.LeaveQueueSlots.Value = 0;
        var candidate = simulation.World.Candidates[0];
        candidate.BeforeNextPurchaseAttempt = _ => simulation.World.EnqueueManualAction();

        simulation.RunFrames(10);

        Assert.Equal(1, candidate.PurchaseCalls);
        Assert.Equal(0, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.ManualQueueCount);
        Assert.Equal(1, simulation.World.QueueCount);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CostRiseImmediatelyBeforeSubmit_IsRejectedAgainstLiveCost()
    {
        using var simulation = new AutoBuySimulation(
            4,
            new[] { new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure, baseCost: 1.0) },
            initialResourceQuantity: 50.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        var candidate = simulation.World.Candidates[0];
        candidate.BeforeNextPurchaseAttempt = item => item.CostMultiplier = 100.0;

        simulation.RunFrames(10);

        Assert.Equal(0, simulation.World.TotalSubmitted);
        Assert.Equal(50.0, simulation.World.ResourceQuantity, 6);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ExternalResourceSpendImmediatelyBeforeSubmit_CannotCreateNegativeBalance()
    {
        using var simulation = new AutoBuySimulation(
            4,
            new[] { new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure, baseCost: 10.0) },
            initialResourceQuantity: 50.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        var candidate = simulation.World.Candidates[0];
        candidate.BeforeNextPurchaseAttempt = _ => simulation.World.ResourceQuantity = 0.0;

        simulation.RunFrames(10);

        Assert.Equal(0, simulation.World.TotalSubmitted);
        Assert.Equal(0.0, simulation.World.ResourceQuantity, 6);

        simulation.SetResourceQuantity("resource", new BigAmount(20.0, 0));
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 1, maximumFrames: 60));
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AvailabilityFlipDuringRankedPass_SkipsStaleCandidateAndPreservesOrder()
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec("a", AutoBuyCandidateKind.Structure, baseCost: 1.0),
            new SimulatedCandidateSpec("b", AutoBuyCandidateKind.Structure, baseCost: 2.0),
            new SimulatedCandidateSpec("c", AutoBuyCandidateKind.Upgrade, baseCost: 3.0, maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(8, specs, initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.World.Candidates[0].BeforeNextPurchaseAttempt = _ =>
            simulation.World.Candidates[1].Available = false;

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 2, maximumFrames: 30));

        Assert.Equal(new[] { "a", "c" }, simulation.World.SubmissionOrder.Take(2));
        Assert.Equal(0, simulation.World.Candidates[1].PurchaseCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReplacementDuringPreparedGroup_DiscardsStaleWrapperWork()
    {
        using var simulation = CreateGroupedStructureSimulation(queueCapacity: 8);
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 1, maximumFrames: 20));
        var staleCandidate = simulation.World.Candidates[0];
        Assert.Equal(1, staleCandidate.PurchaseCalls);

        simulation.ReloadLifecycle(
            clearQueue: true,
            replaceNativeIdentities: false,
            replaceCandidateWrappers: true);
        var replacement = simulation.World.Candidates[0];

        Assert.NotSame(staleCandidate, replacement);
        Assert.True(simulation.RunUntil(world => world.TotalSubmitted == 2, maximumFrames: 30));
        Assert.Equal(1, staleCandidate.PurchaseCalls);
        Assert.True(replacement.PurchaseCalls > 0);
        Assert.Equal(1, simulation.LifecycleGeneration);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ExactReserveThresholdChatter_IsBoundedAndDoesNotMissCrossings()
    {
        using var simulation = new AutoBuySimulation(
            2,
            new[]
            {
                new SimulatedCandidateSpec(
                    "structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0,
                    maximumLevel: 6),
            },
            initialResourceQuantity: 109.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.AbsoluteReserve.Value = "100";
        var candidate = simulation.World.Candidates[0];

        for (var crossing = 0; crossing < 6; crossing++)
        {
            var submissionsBefore = simulation.World.TotalSubmitted;
            simulation.SetResourceQuantity("resource", new BigAmount(109.0, 0));
            simulation.RunFrames(2);
            Assert.Equal(submissionsBefore, simulation.World.TotalSubmitted);

            simulation.SetResourceQuantity("resource", new BigAmount(110.0, 0));
            Assert.True(simulation.RunUntil(
                world => world.TotalSubmitted == submissionsBefore + 1,
                maximumFrames: 60));
            simulation.Step(completionsBeforeTick: 1);
        }

        Assert.Equal(6, simulation.World.TotalSubmitted);
        Assert.True(candidate.CanPurchaseCalls <= 60,
            $"Threshold chatter used {candidate.CanPurchaseCalls} candidate evaluations.");
        Assert.True(simulation.Metrics.MaximumPurchasesInFrame <= 1);
    }

    private static AutoBuySimulation CreateGroupedStructureSimulation(int queueCapacity)
    {
        var simulation = new AutoBuySimulation(
            queueCapacity,
            new[]
            {
                new SimulatedCandidateSpec(
                    "structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 1.0,
                    maximumLevel: 20),
            });
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.RepeatWhileAffordable.Value = false;
        simulation.Config.StructureRepeatMode.Value = AutoBuyStructureRepeatMode.Fixed;
        simulation.Config.FixedStructureLevelsPerCandidate.Value = 6;
        return simulation;
    }
}
