using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

[Trait("Category", "AutoBuyReliability")]
public sealed class AutoBuySimulationFailureTests
{
    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void QueueSnapshotOutage_FailsClosedAndRecoversWithoutLifecycleReset()
    {
        using var simulation = CreateTwoCandidateSimulation();
        simulation.Catalog.QueueReadSucceeds = false;

        simulation.RunFrames(20);

        Assert.Equal(0, simulation.World.TotalSubmitted);
        Assert.Equal(100.0, simulation.World.ResourceQuantity, 6);

        simulation.Catalog.QueueReadSucceeds = true;

        Assert.True(simulation.RunUntil(world => world.TotalSubmitted > 0, maximumFrames: 20));
        Assert.Equal(0, simulation.LifecycleGeneration);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CapacityBelowOccupancy_PausesUntilTheSnapshotBecomesConsistent()
    {
        using var simulation = CreateTwoCandidateSimulation(queueCapacity: 8);
        simulation.Config.LeaveQueueSlots.Value = 0;
        Assert.True(simulation.RunUntil(world => world.QueueCount == 6, maximumFrames: 30));
        var submittedBeforeContradiction = simulation.World.TotalSubmitted;

        simulation.World.SetQueueCapacity(4);
        simulation.RunFrames(20);

        Assert.Equal(submittedBeforeContradiction, simulation.World.TotalSubmitted);
        Assert.Equal(6, simulation.World.QueueCount);

        Assert.Equal(3, simulation.World.Complete(3));
        simulation.World.SetQueueCapacity(5);
        simulation.NotifyProgressionChanged();

        Assert.True(simulation.RunUntil(world => world.QueueCount == 5, maximumFrames: 20));
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData((int)SimulatedPurchaseFailureMode.RejectBeforeMutation)]
    [InlineData((int)SimulatedPurchaseFailureMode.CaughtExceptionBeforeMutation)]
    public void PreMutationFailure_PreservesStateAndHealthySiblingProgresses(int modeValue)
    {
        using var simulation = CreateFailureAndHealthySimulation(
            (SimulatedPurchaseFailureMode)modeValue);
        var failing = simulation.World.Candidates[0];

        Assert.True(simulation.RunUntil(
            _ => failing.PurchaseCalls > 0,
            maximumFrames: 30));
        Assert.Equal(0, simulation.World.TotalSubmitted);
        Assert.Equal(100.0, simulation.World.ResourceQuantity, 6);

        failing.Available = false;
        simulation.NotifyProgressionChanged();

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 60));

        Assert.DoesNotContain("failing", simulation.World.SubmissionOrder);
        Assert.True(failing.PurchaseCalls > 0);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData((int)SimulatedPurchaseFailureMode.MutateThenReportFailure)]
    [InlineData((int)SimulatedPurchaseFailureMode.CaughtExceptionAfterMutation)]
    public void AmbiguousPostMutationFailure_BlocksRetriesUntilLifecycleRecovery(int modeValue)
    {
        using var simulation = CreateFailureAndHealthySimulation(
            (SimulatedPurchaseFailureMode)modeValue);
        var failing = simulation.World.Candidates[0];

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 30));
        simulation.RunFrames(20);

        Assert.True(failing.MutationBlocked);
        Assert.Equal(1, failing.PurchaseCalls);
        Assert.Equal(1, simulation.World.SubmissionOrder.Count(uuid => uuid == "failing"));

        simulation.ReloadLifecycle(
            clearQueue: true,
            replaceNativeIdentities: false,
            replaceCandidateWrappers: true);
        var replacement = simulation.World.Candidates[0];
        Assert.True(simulation.RunUntil(
            _ => replacement.PurchaseCalls == 1,
            maximumFrames: 100));

        Assert.Equal(1, simulation.LifecycleGeneration);
        Assert.NotSame(failing, replacement);
        Assert.True(failing.MutationBlocked);
        Assert.Equal(1, failing.PurchaseCalls);
        Assert.True(replacement.MutationBlocked);
        Assert.Equal(2, simulation.World.SubmissionOrder.Count(uuid => uuid == "failing"));
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData((int)SimulatedCostObservationMode.Unresolved)]
    [InlineData((int)SimulatedCostObservationMode.NegativeCost)]
    [InlineData((int)SimulatedCostObservationMode.NegativeQuantity)]
    [InlineData((int)SimulatedCostObservationMode.MissingResourceIdentity)]
    [InlineData((int)SimulatedCostObservationMode.DuplicateContradictoryResource)]
    public void InvalidCostEvidence_FailsClosedWithoutStarvingHealthySibling(int modeValue)
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec(
                "invalid-cost",
                AutoBuyCandidateKind.Structure,
                baseCost: 1.0,
                costObservationMode: (SimulatedCostObservationMode)modeValue),
            new SimulatedCandidateSpec(
                "healthy",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 10.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(8, specs, initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 40));

        Assert.DoesNotContain("invalid-cost", simulation.World.SubmissionOrder);
        Assert.Equal(90.0, simulation.World.ResourceQuantity, 6);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void PartialMultiResourceObservationFailure_RejectsTheWholeVector()
    {
        var costs = new[]
        {
            new SimulatedResourceCost("resource", "Resource", new BigAmount(10.0, 0)),
            new SimulatedResourceCost("secondary", "Secondary", new BigAmount(5.0, 0)),
        };
        var specs = new[]
        {
            new SimulatedCandidateSpec(
                "partial-vector",
                AutoBuyCandidateKind.Structure,
                costObservationMode: SimulatedCostObservationMode.Unresolved,
                resourceCosts: costs),
            new SimulatedCandidateSpec(
                "healthy",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 10.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(8, specs, initialResourceQuantity: 100.0);
        simulation.World.SetResourceQuantity("secondary", new BigAmount(100.0, 0));
        simulation.Config.LeaveQueueSlots.Value = 0;

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 40));

        Assert.DoesNotContain("partial-vector", simulation.World.SubmissionOrder);
        Assert.Equal(100.0, simulation.World.GetResourceQuantity("secondary").DivideApprox(new BigAmount(1.0, 0)), 6);
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UnknownAvailabilityOrAdmissionContract_FailsClosed(
        bool availabilityReadSucceeds,
        bool nativeContractComplete)
    {
        using var simulation = CreateTwoCandidateSimulation();
        var unsafeCandidate = simulation.World.Candidates[0];
        unsafeCandidate.AvailabilityReadSucceeds = availabilityReadSucceeds;
        unsafeCandidate.NativeContractComplete = nativeContractComplete;

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 40));

        Assert.DoesNotContain("structure", simulation.World.SubmissionOrder);
    }

    [Theory]
    [Trait("Category", "HeadlessE2E")]
    [InlineData((int)SimulatedLifecycleObservationMode.Unavailable)]
    [InlineData((int)SimulatedLifecycleObservationMode.NegativeCurrentLevel)]
    [InlineData((int)SimulatedLifecycleObservationMode.NegativeQueuedLevels)]
    [InlineData((int)SimulatedLifecycleObservationMode.MaxWithoutFiniteLevels)]
    [InlineData((int)SimulatedLifecycleObservationMode.MaxLevelWithQueuedLevels)]
    [InlineData((int)SimulatedLifecycleObservationMode.MaxLevelWithoutMaxQueued)]
    public void InvalidLifecycleEvidence_QuarantinesCandidateAndPreservesSiblingProgress(int modeValue)
    {
        var specs = new[]
        {
            new SimulatedCandidateSpec(
                "invalid-lifecycle",
                AutoBuyCandidateKind.Structure,
                baseCost: 1.0,
                maximumLevel: 10,
                lifecycleObservationMode: (SimulatedLifecycleObservationMode)modeValue),
            new SimulatedCandidateSpec(
                "healthy",
                AutoBuyCandidateKind.Upgrade,
                baseCost: 10.0,
                maximumLevel: 1),
        };
        using var simulation = new AutoBuySimulation(8, specs, initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;

        Assert.True(simulation.RunUntil(
            world => world.SubmissionOrder.Contains("healthy"),
            maximumFrames: 50));

        Assert.DoesNotContain("invalid-lifecycle", simulation.World.SubmissionOrder);
    }

    private static AutoBuySimulation CreateTwoCandidateSimulation(int queueCapacity = 8)
    {
        var simulation = new AutoBuySimulation(
            queueCapacity,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure, baseCost: 10.0),
                new SimulatedCandidateSpec("healthy", AutoBuyCandidateKind.Upgrade, baseCost: 10.0, maximumLevel: 1),
            },
            initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        return simulation;
    }

    private static AutoBuySimulation CreateFailureAndHealthySimulation(
        SimulatedPurchaseFailureMode failureMode)
    {
        var simulation = new AutoBuySimulation(
            8,
            new[]
            {
                new SimulatedCandidateSpec(
                    "failing",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 1.0,
                    failureMode: failureMode),
                new SimulatedCandidateSpec(
                    "healthy",
                    AutoBuyCandidateKind.Upgrade,
                    baseCost: 10.0,
                    maximumLevel: 1),
            },
            initialResourceQuantity: 100.0);
        simulation.Config.LeaveQueueSlots.Value = 0;
        return simulation;
    }
}
