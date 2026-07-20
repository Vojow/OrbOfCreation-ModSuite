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
[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuyBurstTests
{
    [Fact]
    public void FastNativePurchases_UseBoundedCallCapAndPreserveBulkThreeFairness()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("fast", 12, maximumLevel: 100),
            queueCapacity: 32,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 3;

        simulation.Step();

        Assert.Equal(AutoBuyEngine.MaximumNativePurchasesPerFrame, simulation.World.TotalSubmitted);
        Assert.Equal(AutoBuyEngine.MaximumNativePurchasesPerFrame, simulation.Metrics.MaximumPurchasesInFrame);
        Assert.True(MaximumConsecutiveSubmissions(simulation.World.SubmissionOrder) <= 3);
        AssertWorldSafe(simulation);
    }

    [Theory]
    [InlineData(1.1, 1)]
    [InlineData(0.6, 2)]
    [InlineData(0.3, 4)]
    [InlineData(0.13, 8)]
    [InlineData(0.05, 16)]
    public void ModeledPurchaseCost_AdaptsBurstWithinCpuAndOperationCaps(
        double purchaseCostMilliseconds,
        int expectedPurchases)
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("budget", 1, maximumLevel: 100),
            queueCapacity: 32,
            purchaseCostMilliseconds: purchaseCostMilliseconds);
        simulation.Catalog.BulkDevelopment = 100;

        simulation.Step();

        Assert.Equal(expectedPurchases, simulation.World.TotalSubmitted);
        Assert.Equal(expectedPurchases, simulation.Metrics.MaximumPurchasesInFrame);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void QueueReservation_ClampsFastBurstToUsableRoom()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("reservation", 1, maximumLevel: 100),
            queueCapacity: 10,
            purchaseCostMilliseconds: 0.05);
        simulation.Config.LeaveQueueSlots.Value = 2;
        simulation.Catalog.BulkDevelopment = 100;

        simulation.Step();

        Assert.Equal(8, simulation.World.TotalSubmitted);
        Assert.Equal(8, simulation.World.QueueCount);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void FastBurst_AdvancesAcrossFiniteUpgradesOneLevelEach()
    {
        var specs = Enumerable.Range(0, 20)
            .Select(index => new SimulatedCandidateSpec(
                $"burst-upgrade-{index:00}",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + index,
                maximumLevel: 1))
            .ToArray();
        using var simulation = CreateBurstSimulation(
            specs,
            queueCapacity: 32,
            purchaseCostMilliseconds: 0.05);

        simulation.Step();

        Assert.Equal(AutoBuyEngine.MaximumNativePurchasesPerFrame, simulation.World.TotalSubmitted);
        Assert.Equal(AutoBuyEngine.MaximumNativePurchasesPerFrame, simulation.World.DistinctCandidatesSubmitted);
        Assert.All(simulation.World.Candidates, candidate =>
            Assert.InRange(candidate.CurrentLevel + candidate.QueuedLevels, 0, 1));
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void RisingCostsAndReserve_StopPartialGroupInsideBurst()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 20,
            new[]
            {
                new SimulatedCandidateSpec(
                    "burst-threshold",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0,
                    costScaling: 2.0,
                    maximumLevel: 100),
            },
            initialResourceQuantity: 74.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 0.05);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Config.AbsoluteReserve.Value = "5";
        simulation.Catalog.BulkDevelopment = 100;

        simulation.Step();

        Assert.Equal(2, simulation.World.TotalSubmitted);
        Assert.Equal(44.0, simulation.World.ResourceQuantity, precision: 6);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void CapacityShrinkAfterFirstPurchase_StopsRemainingBurstWithoutOverfill()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("shrink", 1, maximumLevel: 100),
            queueCapacity: 20,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 100;
        var candidate = Assert.Single(simulation.World.Candidates);
        candidate.AfterSuccessfulPurchase = _ =>
            simulation.World.SetQueueCapacity(simulation.World.QueueCount);

        simulation.Step();

        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(simulation.World.QueueCapacity, simulation.World.QueueCount);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void EmergencyDisableAfterFirstPurchase_StopsRemainingBurstImmediately()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("emergency", 1, maximumLevel: 100),
            queueCapacity: 20,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 100;
        var candidate = Assert.Single(simulation.World.Candidates);
        candidate.AfterSuccessfulPurchase = _ => simulation.SetEmergencyDisabled(true);

        simulation.Step();

        Assert.Equal(1, simulation.World.TotalSubmitted);
        simulation.RunFrames(20, completionsPerFrame: 1);
        Assert.Equal(1, simulation.World.TotalSubmitted);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void OwnershipLossAfterFirstPurchase_StopsRemainingBurst()
    {
        var ownsStructures = true;
        using var simulation = new AutoBuySimulation(
            queueCapacity: 20,
            BuildStructures("ownership", 1, maximumLevel: 100),
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 0.05,
            ownsActionFamily: kind =>
                kind != AutoBuyCandidateKind.Structure || ownsStructures);
        simulation.Config.LeaveQueueSlots.Value = 0;
        simulation.Catalog.BulkDevelopment = 100;
        var candidate = Assert.Single(simulation.World.Candidates);
        candidate.AfterSuccessfulPurchase = _ => ownsStructures = false;

        simulation.Step();

        Assert.Equal(1, simulation.World.TotalSubmitted);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void LifecycleChangeAfterFirstPurchase_StopsStaleBurstAndFreshGenerationResumes()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("lifecycle", 1, maximumLevel: 100),
            queueCapacity: 20,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 100;
        var candidate = Assert.Single(simulation.World.Candidates);
        candidate.AfterSuccessfulPurchase = _ => simulation.ReloadLifecycle(
            clearQueue: false,
            replaceNativeIdentities: false);

        simulation.Step();

        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.LifecycleGeneration);
        candidate.AfterSuccessfulPurchase = null;
        Assert.True(simulation.RunUntil(
            world => world.TotalSubmitted > 1,
            maximumFrames: 30));
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void AmbiguousSecondMutation_EndsBurstAndRemainsLifecycleBlocked()
    {
        using var simulation = CreateBurstSimulation(
            BuildStructures("ambiguous", 1, maximumLevel: 100),
            queueCapacity: 20,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 100;
        var candidate = Assert.Single(simulation.World.Candidates);
        var submitted = 0;
        candidate.AfterSuccessfulPurchase = current =>
        {
            submitted++;
            if (submitted == 2)
            {
                current.FailureMode = SimulatedPurchaseFailureMode.MutateThenReportFailure;
            }
        };

        simulation.Step();

        Assert.Equal(2, simulation.World.TotalSubmitted);
        Assert.True(candidate.MutationBlocked);
        simulation.RunFrames(20, completionsPerFrame: 1);
        Assert.Equal(2, simulation.World.TotalSubmitted);
        AssertWorldSafe(simulation);
    }

    [Fact]
    public void EightCompletionsPerFrame_AreRefilledByBoundedBursts()
    {
        var specs = BuildStructures("turnover", 130, maximumLevel: 1_000).ToArray();
        using var simulation = CreateBurstSimulation(
            specs,
            queueCapacity: 304,
            purchaseCostMilliseconds: 0.05);
        simulation.Catalog.BulkDevelopment = 3;

        simulation.RunFrames(80);
        for (var frame = 0; frame < 1_200; frame++)
        {
            simulation.Step(completionsBeforeTick: 8);
        }

        Assert.Equal(304, simulation.World.QueueHighWater);
        Assert.Equal(specs.Length, simulation.World.DistinctCandidatesSubmitted);
        Assert.Equal(AutoBuyEngine.MaximumNativePurchasesPerFrame, simulation.Metrics.MaximumPurchasesInFrame);
        Assert.True(MaximumConsecutiveSubmissions(simulation.World.SubmissionOrder) <= 3);
        Assert.True(simulation.World.TotalSubmitted >= 8_500,
            $"Only {simulation.World.TotalSubmitted} purchases were submitted under eight-per-frame turnover.");
        Assert.True(simulation.Metrics.MinimumQueueAfterSaturation >= 260,
            $"Queue depth fell to {simulation.Metrics.MinimumQueueAfterSaturation} after saturation.");
        Assert.True(simulation.World.TotalCandidateEvaluations <= 80_000);
        AssertWorldSafe(simulation);
    }

    private static AutoBuySimulation CreateBurstSimulation(
        IEnumerable<SimulatedCandidateSpec> specs,
        int queueCapacity,
        double purchaseCostMilliseconds)
    {
        var simulation = new AutoBuySimulation(
            queueCapacity,
            specs,
            initialResourceQuantity: 1_000_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: purchaseCostMilliseconds);
        simulation.Config.LeaveQueueSlots.Value = 0;
        return simulation;
    }

    private static IEnumerable<SimulatedCandidateSpec> BuildStructures(
        string prefix,
        int count,
        int maximumLevel)
    {
        for (var index = 0; index < count; index++)
        {
            yield return new SimulatedCandidateSpec(
                $"{prefix}-structure-{index:000}",
                AutoBuyCandidateKind.Structure,
                baseCost: 1.0 + (index % 11),
                costScaling: 1.001,
                maximumLevel: maximumLevel);
        }
    }

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

    private static void AssertWorldSafe(AutoBuySimulation simulation)
    {
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
        Assert.True(simulation.World.TotalAutomationCompleted <= simulation.World.TotalSubmitted);
        Assert.InRange(
            simulation.Metrics.MaximumPurchasesInFrame,
            0,
            AutoBuyEngine.MaximumNativePurchasesPerFrame);
    }
}
