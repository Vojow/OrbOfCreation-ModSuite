using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Tests.Simulation;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuySimulationE2ETests
{
    [Fact]
    public void CompletionSettlementGate_CoalescesSignalsUntilTheActivePassFinishes()
    {
        var gate = new AutoBuyCompletionSettlementGate();

        gate.Notify();
        Assert.True(gate.TryBegin(settlementInProgress: false));
        Assert.False(gate.TryBegin(settlementInProgress: false));

        gate.Notify();
        gate.Notify();
        Assert.False(gate.TryBegin(settlementInProgress: true));
        Assert.True(gate.TryBegin(settlementInProgress: false));
        Assert.False(gate.TryBegin(settlementInProgress: false));
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void QueueAdapter_ReadsSharedActionQueueInsteadOfNativeAutoBuyQueue()
    {
        ActionManager.RemainingRoom = 203;
        ActionManager.instance.actionableItems.maxQueuedItems.Value = 304;
        AutoBuyManager.RemainingRoom = 11;
        using var catalog = new ReflectionAutoBuyCatalog();

        var succeeded = catalog.TryCaptureQueueCapacity(
            automationUsageLimit: int.MaxValue,
            manualReservation: 1,
            out var snapshot);

        Assert.True(succeeded);
        Assert.Equal(304, snapshot.NativeCapacity);
        Assert.Equal(203, snapshot.NativeRemainingRoom);
        Assert.Equal(101, snapshot.LiveOccupancy);
        Assert.Equal(202, snapshot.UsableAutomationRoom);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LoneCandidate_FillsEveryUsableNativeQueueSlot()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            new[]
            {
                new SimulatedCandidateSpec("only-structure", AutoBuyCandidateKind.Structure),
            });

        var filled = simulation.RunUntil(
            world => world.QueueCount == 303,
            maximumFrames: 320);

        Assert.True(filled, "The single affordable candidate did not fill the simulated native queue.");
        Assert.Equal(303, simulation.World.QueueCount);
        Assert.Equal(303, simulation.World.TotalSubmitted);
        Assert.Equal(303, simulation.World.QueueHighWater);
        Assert.Equal(303, simulation.World.Candidates[0].QueuedLevels);
        Assert.Equal(0, simulation.World.TotalCompleted);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void ManualActionsAndAutomationShareTheSameQueueCapacity()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });
        for (var i = 0; i < 100; i++)
        {
            simulation.World.EnqueueManualAction();
        }

        var filled = simulation.RunUntil(
            world => world.QueueCount == 303,
            maximumFrames: 220);

        Assert.True(filled, "Automation did not consume the room remaining beside manual actions.");
        Assert.Equal(303, simulation.World.QueueCount);
        Assert.Equal(203, simulation.World.TotalSubmitted);
        Assert.Equal(203, simulation.World.Candidates[0].QueuedLevels);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReload_ReplacesNativeIdentitiesAndResumesFromAuthoritativeState()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 8,
            new[]
            {
                new SimulatedCandidateSpec("always-ready", AutoBuyCandidateKind.Structure, baseCost: 10.0),
                new SimulatedCandidateSpec(
                    "unlocks-after-load",
                    AutoBuyCandidateKind.Upgrade,
                    baseCost: 10.0,
                    available: false),
            },
            initialResourceQuantity: 45.0);

        simulation.RunFrames(20);

        var first = simulation.World.Candidates[0];
        var unlockedAfterLoad = simulation.World.Candidates[1];
        Assert.Equal(4, simulation.World.TotalSubmitted);
        Assert.Equal(5.0, simulation.World.ResourceQuantity, 6);
        Assert.Equal(0, unlockedAfterLoad.PurchaseCalls);

        var firstIdentity = first.NativeIdentity;
        var secondIdentity = unlockedAfterLoad.NativeIdentity;
        simulation.World.ResourceQuantity = 100.0;
        unlockedAfterLoad.Available = true;
        simulation.ReloadLifecycle();

        Assert.NotSame(firstIdentity, first.NativeIdentity);
        Assert.NotSame(secondIdentity, unlockedAfterLoad.NativeIdentity);
        Assert.Equal(0, simulation.World.QueueCount);

        var refilled = simulation.RunUntil(
            world => world.QueueCount == 7,
            maximumFrames: 30);

        Assert.True(refilled, "Auto Buy did not resume after the simulated save/load lifecycle.");
        Assert.Contains("unlocks-after-load", simulation.World.SubmissionOrder);
        Assert.True(unlockedAfterLoad.PurchaseCalls > 0);
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void AmbiguousNativeFailure_FailsClosedWithoutCorruptingQueueAccounting()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 12,
            new[]
            {
                new SimulatedCandidateSpec(
                    "ambiguous",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 10.0,
                    failureMode: SimulatedPurchaseFailureMode.MutateThenReportFailure),
                new SimulatedCandidateSpec("healthy", AutoBuyCandidateKind.Upgrade, baseCost: 20.0),
            },
            initialResourceQuantity: 100.0);

        simulation.RunFrames(1);

        var ambiguous = simulation.World.Candidates[0];
        var healthy = simulation.World.Candidates[1];
        Assert.Equal(1, ambiguous.PurchaseCalls);
        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.QueueCount);
        Assert.Equal(90.0, simulation.World.ResourceQuantity, 6);
        Assert.Equal(0, healthy.PurchaseCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionCostIncrease_RefreshesReserveEvidenceBeforeImmediateRefill()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 4,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure, baseCost: 10.0),
            },
            initialResourceQuantity: 200.0);
        simulation.Config.AbsoluteReserve.Value = "100";

        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        simulation.RunFrames(5);
        var candidate = simulation.World.Candidates[0];
        Assert.Equal(3, simulation.World.TotalSubmitted);
        Assert.Equal(170.0, simulation.World.ResourceQuantity, 6);

        candidate.CostMultiplier = 8.0;
        simulation.Step(completionsBeforeTick: 1);

        Assert.Equal(3, simulation.World.TotalSubmitted);
        Assert.Equal(2, simulation.World.QueueCount);
        Assert.Equal(170.0, simulation.World.ResourceQuantity, 6);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionWake_FailsClosedWhenManualActionConsumesTheReopenedSlot()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 4,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });

        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        simulation.RunFrames(5);
        var purchasesBefore = simulation.World.TotalSubmitted;

        simulation.Step(
            completionsBeforeTick: 1,
            afterCompletions: world => world.EnqueueManualAction());

        Assert.Equal(purchasesBefore, simulation.World.TotalSubmitted);
        Assert.Equal(3, simulation.World.QueueCount);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReload_DoesNotReuseAnOldCompletionRefreshGeneration()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 4,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure, baseCost: 10.0),
            },
            initialResourceQuantity: 200.0);
        simulation.Config.AbsoluteReserve.Value = "100";
        var candidate = simulation.World.Candidates[0];

        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        simulation.RunFrames(5);
        candidate.CostMultiplier = 8.0;
        simulation.Step(completionsBeforeTick: 1);
        Assert.Equal(3, simulation.World.TotalSubmitted);

        candidate.CostMultiplier = 1.0;
        simulation.ReloadLifecycle(replaceNativeIdentities: false);
        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
        simulation.RunFrames(5);
        var purchasesBeforeSecondCompletion = simulation.World.TotalSubmitted;

        candidate.CostMultiplier = 8.0;
        simulation.Step(completionsBeforeTick: 1);

        Assert.Equal(purchasesBeforeSecondCompletion, simulation.World.TotalSubmitted);
        Assert.Equal(2, simulation.World.QueueCount);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void StructureBulkCompletion_OneCallbackReleasesMultipleSlotsAndRefillsFromLiveRoom()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 8,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });

        Assert.True(simulation.RunUntil(world => world.QueueCount == 7, maximumFrames: 30));
        simulation.RunFrames(5);
        var submissionsBefore = simulation.World.TotalSubmitted;
        var signalsBefore = simulation.Catalog.CompletionSignals;
        var queueCountObservedBeforeOuterUnstack = -1;

        var completion = simulation.StepNativeCompletion(
            bulkLevels: 3,
            afterSignalBeforeOuterUnstack: world =>
                queueCountObservedBeforeOuterUnstack = world.QueueCount);

        Assert.True(completion.AutomationCompletion);
        Assert.Equal(3, completion.CompletedLevels);
        Assert.Equal(5, completion.QueueCountAtSignal);
        Assert.Equal(5, queueCountObservedBeforeOuterUnstack);
        Assert.Equal(signalsBefore + 1, simulation.Catalog.CompletionSignals);
        Assert.Equal(3, simulation.World.TotalCompleted);
        Assert.Equal(submissionsBefore + 1, simulation.World.TotalSubmitted);
        Assert.Equal(5, simulation.World.QueueCount);
        Assert.True(simulation.RunUntil(world => world.QueueCount == 7, maximumFrames: 2));
        Assert.Equal(submissionsBefore + 3, simulation.World.TotalSubmitted);
        Assert.Equal(7, simulation.World.QueueCount);
        Assert.Equal(7, simulation.World.QueueHighWater);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void StructureEchoCompletion_RequeuesBeforeWakeAndNeverOverfillsLiveRoom()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 8,
            new[]
            {
                new SimulatedCandidateSpec("structure", AutoBuyCandidateKind.Structure),
            });

        Assert.True(simulation.RunUntil(world => world.QueueCount == 7, maximumFrames: 30));
        simulation.RunFrames(5);
        var submissionsBefore = simulation.World.TotalSubmitted;

        var completion = simulation.StepNativeCompletion(bulkLevels: 3, echoActions: 1);

        Assert.Equal(6, completion.QueueCountAtSignal);
        Assert.Equal(3, completion.CompletedLevels);
        Assert.Equal(1, completion.EchoActionsEnqueued);
        Assert.Equal(submissionsBefore + 1, simulation.World.TotalSubmitted);
        Assert.Equal(6, simulation.World.QueueCount);
        Assert.True(simulation.RunUntil(world => world.QueueCount == 7, maximumFrames: 1));
        Assert.Equal(submissionsBefore + 2, simulation.World.TotalSubmitted);
        Assert.Equal(7, simulation.World.QueueCount);
        Assert.True(simulation.World.QueueCount <= simulation.World.QueueCapacity);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void CompletionEffectsChangingAnotherCandidate_AreRevalidatedBeforeRefill()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 3,
            new[]
            {
                new SimulatedCandidateSpec(
                    "finite-structure",
                    AutoBuyCandidateKind.Structure,
                    maximumLevel: 1),
                new SimulatedCandidateSpec("follow-up", AutoBuyCandidateKind.Upgrade),
            });

        Assert.True(simulation.RunUntil(world => world.QueueCount == 2, maximumFrames: 20));
        simulation.RunFrames(5);
        var submissionsBefore = simulation.World.TotalSubmitted;
        var followUp = simulation.World.Candidates.Single(candidate => candidate.Uuid == "follow-up");

        simulation.StepNativeCompletion(
            bulkLevels: 1,
            afterCandidateEffectsBeforeSignal: _ => followUp.Available = false);

        Assert.Equal(submissionsBefore, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.QueueCount);
        Assert.Equal(1, simulation.World.Candidates.Single(candidate =>
            candidate.Uuid == "finite-structure").CurrentLevel);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void MultipleBigResources_AllBalancesRemainAuthoritativeAcrossAdmissionAndSpend()
    {
        var costs = new[]
        {
            new SimulatedResourceCost("essence", "Essence", new BigAmount(1.0, 200)),
            new SimulatedResourceCost(
                "bandwidth",
                "Bandwidth",
                new BigAmount(5.0, 0),
                capacity: new BigAmount(1.0, 1),
                isBandwidth: true),
        };
        using var simulation = new AutoBuySimulation(
            queueCapacity: 5,
            new[]
            {
                new SimulatedCandidateSpec(
                    "multi-resource",
                    AutoBuyCandidateKind.Structure,
                    resourceCosts: costs),
            });
        simulation.World.SetResourceQuantity("essence", new BigAmount(3.0, 200));
        simulation.World.SetResourceQuantity("bandwidth", new BigAmount(9.0, 0));

        simulation.RunFrames(20);

        Assert.Equal(1, simulation.World.TotalSubmitted);
        Assert.Equal(1, simulation.World.QueueCount);
        Assert.Equal(0, simulation.World.GetResourceQuantity("essence").CompareTo(new BigAmount(2.0, 200)));
        Assert.Equal(0, simulation.World.GetResourceQuantity("bandwidth").CompareTo(new BigAmount(4.0, 0)));
        Assert.True(simulation.World.ResourceQuantity >= 0.0);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void BlockedStructure_StaysParkedUntilItsExactResourceThresholdIsCrossed()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 5,
            new[]
            {
                new SimulatedCandidateSpec(
                    "threshold-structure",
                    AutoBuyCandidateKind.Structure,
                    baseCost: 100.0),
            },
            initialResourceQuantity: 50.0);

        simulation.RunFrames(20);
        var evaluationsWhileParked = simulation.Catalog.CompletedCandidateEvaluations;
        Assert.Equal(0, simulation.World.TotalSubmitted);

        simulation.SetResourceQuantity("resource", new BigAmount(99.0, 0));
        simulation.RunFrames(20);
        Assert.Equal(evaluationsWhileParked, simulation.Catalog.CompletedCandidateEvaluations);
        Assert.Equal(0, simulation.World.TotalSubmitted);

        simulation.SetResourceQuantity("resource", new BigAmount(100.0, 0));

        Assert.True(simulation.RunUntil(
            world => world.TotalSubmitted == 1,
            maximumFrames: 120));
        Assert.Equal(0.0, simulation.World.ResourceQuantity, 6);
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void LifecycleReload_ReplacesCandidateWrapperAndRetainsStableTypedIdentity()
    {
        using var simulation = new AutoBuySimulation(
            queueCapacity: 4,
            new[]
            {
                new SimulatedCandidateSpec("stable-id", AutoBuyCandidateKind.Upgrade),
            });
        var before = simulation.World.Candidates.Single();

        simulation.ReloadLifecycle(replaceCandidateWrappers: true);
        var after = simulation.World.Candidates.Single();

        Assert.NotSame(before, after);
        Assert.NotSame(before.NativeIdentity, after.NativeIdentity);
        Assert.Equal(before.Uuid, after.Uuid);
        Assert.Equal(before.Kind, after.Kind);
        Assert.True(simulation.RunUntil(world => world.QueueCount == 3, maximumFrames: 20));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void PeriodicCompletions_KeepPreparedHandoffAndBoundedEvaluationWork()
    {
        var candidates = Enumerable.Range(0, 166)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + (index % 7)))
            .ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            candidates,
            initialResourceQuantity: 1_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);

        for (var frame = 0; frame < 900; frame++)
        {
            var completions = frame >= 400 && (frame - 400) % 20 == 0 ? 1 : 0;
            simulation.Step(completions);
        }

        var evaluations = simulation.World.TotalCandidateEvaluations;
        var purchases = simulation.World.TotalSubmitted;
        var evaluationBudget = (4 * candidates.Length) + (4 * purchases);

        Assert.True(simulation.World.QueueHighWater >= 300,
            $"Queue high-water was only {simulation.World.QueueHighWater}/304.");
        Assert.True(simulation.World.QueueCount >= 295,
            $"Final queue depth collapsed to {simulation.World.QueueCount}/304.");
        Assert.True(purchases >= 325,
            $"Only {purchases} purchases completed within the deterministic frame budget.");
        Assert.True(evaluations <= evaluationBudget,
            $"Candidate work grew to {evaluations} evaluations; budget was {evaluationBudget}.");
        Assert.True(simulation.Metrics.MaximumEvaluationsInFrame <= 55,
            $"One frame evaluated {simulation.Metrics.MaximumEvaluationsInFrame} candidates.");
        Assert.True(simulation.Metrics.IdleFramesWithPurchasableWork <= 90,
            $"Usable queue room was left idle for {simulation.Metrics.IdleFramesWithPurchasableWork} frames.");
        Assert.NotNull(simulation.Metrics.FramesToNinetyPercentQueue);
        Assert.Equal(166, simulation.World.SubmissionOrder.Take(166).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void CompletionStorm_KeepsQueueFullWithoutEvaluationAmplification()
    {
        var candidates = Enumerable.Range(0, 166)
            .Select(index => new SimulatedCandidateSpec(
                $"candidate-{index:000}",
                index % 2 == 0 ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
                baseCost: 1.0 + (index % 7)))
            .ToArray();
        using var simulation = new AutoBuySimulation(
            queueCapacity: 304,
            candidates,
            initialResourceQuantity: 1_000_000_000.0,
            readObservationCostMilliseconds: 0.02,
            purchaseObservationCostMilliseconds: 1.1);

        for (var frame = 0; frame < 900; frame++)
        {
            var completions = frame >= 400 && (frame - 400) % 4 == 0 ? 1 : 0;
            simulation.Step(completions);
        }

        var purchases = simulation.World.TotalSubmitted;
        var evaluationBudget = (4 * candidates.Length) + (4 * purchases);
        Assert.True(simulation.World.QueueCount >= 295,
            $"Final queue depth was {simulation.World.QueueCount}/304.");
        Assert.True(purchases >= 416,
            $"Only {purchases} purchases completed during the storm.");
        Assert.True(simulation.World.TotalCandidateEvaluations <= evaluationBudget,
            $"Candidate work grew to {simulation.World.TotalCandidateEvaluations} evaluations; " +
            $"budget was {evaluationBudget}.");
        Assert.True(simulation.Metrics.IdleFramesWithPurchasableWork <= 40,
            $"Usable queue room was left idle for {simulation.Metrics.IdleFramesWithPurchasableWork} frames.");
    }
}
