using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public AutoBuyCycleActionAdapterTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void Execute_StructurePurchase_CommitsWithVerifiedEvidence()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(4, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_UpgradePurchase_PinsSingleBuyAndCommits()
    {
        global::GlobalVariables.MultiBuy.Value = 5;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var result = Execute(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(3, upgrade.GetQueuedPurchaseLevel());
        // The scope restored the operator's multiplier after the single-level purchase.
        Assert.Equal(5, global::GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void Execute_LifecycleEpochDrift_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// The epoch the boundary trusts is the one the plan's own world was collected under, not the one
    /// the runner was built with.
    /// </summary>
    /// <remarks>
    /// The two come apart in exactly one window, and it is a real one: the game reloads, the collector
    /// reads the new run, and the host has not yet been told to replace the runner. The plan is about
    /// the world it was made from, and that world is the current one — so the purchase stands. Judging
    /// it by the runner's frozen lifecycle would refuse a purchase that is perfectly good, which is
    /// what this used to do.
    /// </remarks>
    [Fact]
    public void Execute_WorldCollectedAheadOfTheRunnersLifecycle_PurchasesAnyway()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch + 1,
            plannedEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(2, structure.queuedQuantity);
    }

    /// <summary>
    /// The mirror of the case above, and the one that matters for safety: a plan made against the run
    /// the game has since left is refused however current the runner believes it is.
    /// </summary>
    [Fact]
    public void Execute_PlanFromAPreviousRunOfTheGame_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            plannedEpoch: PlannedEpoch - 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// A world nobody collected authorises nothing. Zero is the epoch no lifecycle ever has, so it
    /// matches no live reading and the purchase never reaches the game.
    /// </summary>
    [Fact]
    public void Execute_PlanFromAWorldNobodyCollected_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            plannedEpoch: 0);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// The live half of the guard fails closed too: a game reporting no epoch authorises nothing, even
    /// when the plan carries the same zero and the two therefore agree.
    /// </summary>
    [Fact]
    public void Execute_GameReportsNoLifecycleEpoch_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: 0,
            plannedEpoch: 0);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_KindNotSelected_RejectsServiceDisabled()
    {
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            structures: false,
            upgrades: true);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
    }

    [Fact]
    public void Execute_CandidateNotFound_FaultsAdapter()
    {
        var result = Execute(AutoBuyCandidateKind.Structure, Guid.NewGuid(), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    /// <summary>
    /// <c>CanPurchase()</c> is still asked immediately before mutating: it folds in live
    /// requirements and queue room, which move between the plan and the call.
    /// </summary>
    [Fact]
    public void Execute_NotAdmissible_RejectsNative()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
    }

    // ---- A refusal names the term that refused ---------------------------------------------------

    /// <summary>
    /// The price is what refused, and the submission says so.
    /// </summary>
    /// <remarks>
    /// <c>CanPurchase()</c> answers with one bool over several conditions, so "the game said no" is
    /// all a refusal used to carry. Reading the terms the game exposes individually turns that into a
    /// cause a person can act on.
    /// </remarks>
    [Fact]
    public void Submit_RefusedOnPrice_NamesTheCostList()
    {
        var mana = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(3.2, 3),
        };
        var spark = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(1.9, 1),
        };
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        structure.purchaseCost.costs.Add(new global::ResourceTuple(mana, new BigDouble(1.6, 3)));
        structure.purchaseCost.costs.Add(new global::ResourceTuple(spark, new BigDouble(2.0, 1)));
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(AutoBuyAdmissionTerm.Passed, submission.Diagnosis.IsAvailable);
        Assert.Equal(AutoBuyAdmissionTerm.Refused, submission.Diagnosis.HasEnough);
        Assert.Equal("GetPurchaseCost().HasEnough()", submission.Diagnosis.RefusingTerm);
        Assert.Equal(AutoBuyRefusalClassification.AffordabilityChanged, submission.Diagnosis.Classification);
        Assert.True(submission.Diagnosis.LiveCosts.IsComplete);
        var rows = submission.Diagnosis.LiveCosts.Rows;
        Assert.Equal(2, rows.Length);
        Assert.Equal(Guid.Parse(mana.uuid), rows[0].ResourceId);
        Assert.Equal(new BigDouble(1.6, 3), rows[0].Cost);
        Assert.Equal(mana.GetTrueQuantity(), rows[0].Available);
        Assert.Equal(Guid.Parse(spark.uuid), rows[1].ResourceId);
        Assert.Equal(new BigDouble(2.0, 1), rows[1].Cost);
        Assert.Equal(spark.GetTrueQuantity(), rows[1].Available);
    }

    /// <summary>
    /// The exact reported shape — affordable in the plan, price-only refusal at the boundary — is a
    /// pre-native skip. It records the disagreement but does not turn it into a structural rejection.
    /// </summary>
    [Theory]
    [InlineData("2fa66381-76be-42d5-a25b-31cb5790f03a", "cc4f0000-0000-0000-0000-000000000000", 2.0, 1, 2.5335543615217575, 1)]
    [InlineData("30263415-650b-4544-85f9-cff8afdb089b", "55758000-0000-0000-0000-000000000000", 4.5, 57, 2.6160792960345307, 58)]
    public void Execute_PriceOnlyRefusal_SkipsWithoutNativeEvidence(
        string candidateText,
        string resourceText,
        double costMantissa,
        int costExponent,
        double plannedAvailableMantissa,
        int plannedAvailableExponent)
    {
        var candidateId = Guid.Parse(candidateText);
        var resourceId = Guid.Parse(resourceText);
        var resource = new global::ResourceSO
        {
            uuid = resourceId.ToString(),
            quantity = new BigDouble(costMantissa * 0.9, costExponent),
        };
        var upgrade = new global::UpgradeSO
        {
            uuid = candidateId.ToString(),
            available = true,
            purchasable = true,
        };
        var cost = new BigDouble(costMantissa, costExponent);
        upgrade.purchaseCost.costs.Add(new global::ResourceTuple(resource, cost));
        global::UpgradeSO.All.Add(upgrade);
        var belief = Belief(
            resourceId,
            cost,
            new BigDouble(plannedAvailableMantissa, plannedAvailableExponent));
        var refusals = new RecordingRefusals();

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            candidateId,
            nativeEpoch: PlannedEpoch,
            refusals: refusals,
            belief: belief);

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Skipped, result.Code);
        Assert.False(result.HasNativeEvidence);
        var report = Assert.Single(refusals.Reports);
        Assert.Equal(AutoBuyRefusalClassification.AffordabilityChanged, report.Diagnosis.Classification);
        Assert.Equal(resourceId, Assert.Single(report.Diagnosis.LiveCosts.Rows.ToArray()).ResourceId);
    }

    [Fact]
    public void Execute_LaterAffordabilityRefusal_NamesEarlierSameBatchResourceOverlap()
    {
        var shared = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(4.0, 1),
        };
        var first = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        first.purchaseCost.costs.Add(
            new global::ResourceTuple(shared, new BigDouble(2.0, 1)));
        var second = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        second.purchaseCost.costs.Add(
            new global::ResourceTuple(shared, new BigDouble(3.0, 1)));
        global::StructureSO.All.Add(first);
        global::StructureSO.All.Add(second);
        var refusals = new RecordingRefusals();
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            refusals,
            new FixedWorldGeneration(new WorldGeneration(9)));

        var firstResult = adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Structure,
                Guid.Parse(first.uuid),
                PlannedEpoch,
                count: 1,
                Belief(Guid.Parse(shared.uuid), new BigDouble(2.0, 1), shared.GetTrueQuantity()),
                new MonotonicTimestamp(100)),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 0, attemptedAt: 130));
        var secondResult = adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Structure,
                Guid.Parse(second.uuid),
                PlannedEpoch,
                count: 1,
                Belief(Guid.Parse(shared.uuid), new BigDouble(3.0, 1), shared.GetTrueQuantity()),
                new MonotonicTimestamp(100)),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 1, attemptedAt: 150));

        Assert.Equal(ServiceActionDisposition.Committed, firstResult.Disposition);
        Assert.Equal(ServiceActionDisposition.Skipped, secondResult.Disposition);
        var report = Assert.Single(refusals.Reports);
        var earlier = Assert.Single(report.EarlierPurchases.ToArray());
        Assert.Equal(Guid.Parse(first.uuid), earlier.Uuid);
        Assert.Equal(0, earlier.ActionIndex);
        Assert.Equal(1, earlier.CommittedLevels);
        Assert.Equal((ulong)9, report.LatestWorldGeneration);
        Assert.Equal(new MonotonicTimestamp(100), report.WorldCollectedAt);
        Assert.Equal(new MonotonicTimestamp(150), report.AdmissionAttemptedAt);
    }

    [Fact]
    public void Submit_RefusedOnAvailability_NamesAvailability()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = false,
            purchasable = false,
        };
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal("IsAvailable()", submission.Diagnosis.RefusingTerm);
    }

    /// <summary>
    /// The queued-level cap is the term that livelocked a live session while the log blamed the price.
    /// </summary>
    [Fact]
    public void Submit_RefusedOnTheQueuedLevelCap_NamesIt()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 1,
            queuedLevels = 1,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(AutoBuyAdmissionTerm.Passed, submission.Diagnosis.IsMaxLevel);
        Assert.Equal(AutoBuyAdmissionTerm.Refused, submission.Diagnosis.IsMaxQueuedLevel);
        Assert.Equal("IsMaxQueuedLevel()", submission.Diagnosis.RefusingTerm);
    }

    /// <summary>
    /// Every readable term passes and the game still refused, so the cause is the one that cannot be
    /// read parameterlessly — the per-level prerequisites — and the diagnosis says so by elimination
    /// rather than blaming a term that answered.
    /// </summary>
    [Fact]
    public void Submit_RefusedWithEveryReadableTermPassing_LeavesNoTermNamed()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
        };
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(string.Empty, submission.Diagnosis.RefusingTerm);
        Assert.True(submission.Diagnosis.WasAsked);
        Assert.Equal(
            "refused by an unreadable admission term (per-level prerequisites by elimination, " +
            "which the planner modelled as met)",
            submission.Diagnosis.Describe());
    }

    /// <summary>
    /// A refused plan is handed on with both halves of the disagreement: what the worker believed,
    /// and which live term contradicted it.
    /// </summary>
    [Fact]
    public void Execute_NotAdmissible_ReportsTheRefusalWithBothHalves()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 1,
            queuedLevels = 1,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);
        var resourceId = Guid.NewGuid();
        var belief = new AutoBuyPlanBelief(
            isAvailable: true,
            hasFiniteLevels: true,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 1,
            queuedLevels: 0,
            costResourceCount: 1,
            pricedResourceCount: 1,
            costRatio: 0.25,
            bindingResourceId: resourceId,
            bindingIsBandwidth: false,
            bindingCost: new BigDouble(2.0, 0),
            bindingAvailable: new BigDouble(8.0, 0),
            bindingReserveFloor: default);
        var refusals = new RecordingRefusals();

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch,
            refusals: refusals,
            belief: belief);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        var report = Assert.Single(refusals.Reports);
        Assert.Equal(Guid.Parse(upgrade.uuid), report.Uuid);
        Assert.Equal(AutoBuyCandidateKind.Upgrade, report.Kind);
        Assert.Equal("IsMaxQueuedLevel()", report.Diagnosis.RefusingTerm);
        // The plan believed there was room; the game says the queue already reached the cap.
        Assert.False(report.Belief.IsMaxQueuedLevel);
        Assert.Equal(0.25, report.Belief.CostRatio);
        Assert.Equal(resourceId, report.Belief.BindingResourceId);
        Assert.Equal(PlannedEpoch, report.CollectedAtEpoch);
        Assert.Equal((ulong)1, report.WorldGeneration);
        Assert.Equal((ulong)1, report.ConfigGeneration);
    }

    [Fact]
    public void Execute_CommittedPurchase_ReportsNoRefusal()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var refusals = new RecordingRefusals();

        Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            refusals: refusals);

        Assert.Empty(refusals.Reports);
    }

    /// <summary>
    /// Running out of queue room is the operator's reserve doing its job, not the planner being
    /// wrong, so it stands nothing down.
    /// </summary>
    [Fact]
    public void Execute_QueueReserveReached_ReportsNoRefusal()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var refusals = new RecordingRefusals();

        Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 2,
            remainingRoom: 2,
            refusals: refusals);

        Assert.Empty(refusals.Reports);
    }

    private sealed class RecordingRefusals : IAutoBuyRefusalResponsePort
    {
        internal System.Collections.Generic.List<AutoBuyRefusalReport> Reports { get; } = new();

        public void ObserveRefusal(in AutoBuyRefusalReport report) => Reports.Add(report);
    }

    /// <summary>
    /// Availability is decided from the snapshot, not re-read here.
    /// </summary>
    /// <remarks>
    /// <c>WorldStructure.Unlocked</c> is what the worker admits a candidate on, so an unavailable
    /// candidate never becomes an action. The boundary asking again was a second answer to a settled
    /// question; this pins that it is gone, because a boundary that still read
    /// <c>IsAvailable()</c> would reject this submission.
    /// </remarks>
    [Fact]
    public void Execute_AvailabilityIsNotReReadAtTheBoundary()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = false,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_MutationDidNotApply_SkipsWithZeroCommitEvidence()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
            ApplyPurchaseMutation = false,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Skipped, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.NotEqual(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(1, result.NativeEvidence.CallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeEvidence.CallOutcome.MutationsCommitted);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Submit_UpgradeBulk_CommitsEveryRequestedLevelAndRestoresMultiplier()
    {
        global::GlobalVariables.MultiBuy.Value = 7;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 3);

        Assert.True(submission.Verified);
        Assert.Equal(3, submission.RequestedLevels);
        Assert.Equal(3, submission.CommittedLevels);
        Assert.Equal(3, upgrade.GetQueuedPurchaseLevel());
        // The scope restored the operator's multiplier after the bulk call.
        Assert.Equal(7, global::GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void Submit_UpgradeBulk_PartialPurchaseIsStillVerified()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        // The multiplier requests three levels but the upgrade caps at two: "bought 2 of 3".
        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 3);

        Assert.True(submission.Verified);
        Assert.Equal(3, submission.RequestedLevels);
        Assert.Equal(2, submission.CommittedLevels);
        Assert.Equal(2, upgrade.GetQueuedPurchaseLevel());
    }

    [Fact]
    public void Submit_UpgradeSingle_CommitsExactlyOneLevel()
    {
        global::GlobalVariables.MultiBuy.Value = 9;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedLevels = 4,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 1);

        Assert.True(submission.Verified);
        Assert.Equal(1, submission.RequestedLevels);
        Assert.Equal(1, submission.CommittedLevels);
        Assert.Equal(5, upgrade.GetQueuedPurchaseLevel());
        Assert.Equal(9, global::GlobalVariables.MultiBuy.Value);
    }

    /// <summary>
    /// A structure asked for several levels queues several, one native call each.
    /// </summary>
    /// <remarks>
    /// The native structure purchase forces exactly one level and consults no multiplier, so bulk
    /// development is the same call repeated — which is why the count used to be discarded here and
    /// the game's own bulk-build setting did nothing.
    /// </remarks>
    [Fact]
    public void Submit_StructureBulk_QueuesEveryRequestedLevel()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 5);

        Assert.True(submission.Verified);
        Assert.Equal(5, submission.RequestedLevels);
        Assert.Equal(5, submission.CommittedLevels);
        Assert.Equal(7, structure.queuedQuantity);
    }

    [Fact]
    public void Submit_StructureSingle_QueuesExactlyOne()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.True(submission.Verified);
        Assert.Equal(1, submission.RequestedLevels);
        Assert.Equal(1, submission.CommittedLevels);
        Assert.Equal(3, structure.queuedQuantity);
    }

    /// <summary>
    /// A group that runs out partway is a partial success, not a refusal.
    /// </summary>
    /// <remarks>
    /// Each level past the first is guarded by the game's own admission check, so a group that stops
    /// early stops because the game said so. Treating that as a failed submission would stand the
    /// service down over the ordinary end of a bulk buy.
    /// </remarks>
    [Fact]
    public void Submit_StructureBulk_StopsWhenTheGameStopsAdmitting()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
        };
        // The holdings cover three levels; the fourth is where the game says no.
        structure.purchaseCost.AffordableLevels = 3;
        global::StructureSO.All.Add(structure);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 6);

        Assert.True(submission.Verified);
        Assert.Equal(6, submission.RequestedLevels);
        Assert.Equal(3, submission.CommittedLevels);
        Assert.Equal(3, structure.queuedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_RejectsNonPositiveCount(int count)
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = new AutoBuyNativePurchaseAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count);

        Assert.False(submission.Verified);
        Assert.Equal(AutoBuyPurchasePreflight.CandidateUnavailable, submission.Preflight);
        Assert.Equal(0, upgrade.GetQueuedPurchaseLevel());
    }

    [Fact]
    public void Execute_QueueReserveReached_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        // Only the reserved slots remain free, so no purchase may be submitted.
        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 2,
            remainingRoom: 2);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(2, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_RoomAboveReserve_ProceedsToPurchase()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 1,
            remainingRoom: 2);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_QueueRoomUnavailable_FaultsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            roomReadable: false);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(3, structure.queuedQuantity);
    }

    /// <param name="nativeEpoch">What the live game says its epoch is when the action is submitted.</param>
    /// <param name="plannedEpoch">
    /// The epoch the world this purchase was planned from was collected under, which the action
    /// carries to the boundary. Defaults to agreeing with the cycle's own lifecycle, which is the
    /// ordinary case; a test that wants them to disagree says so.
    /// </param>
    private static ServiceActionResult Execute(
        AutoBuyCandidateKind kind,
        Guid uuid,
        long nativeEpoch,
        long plannedEpoch = PlannedEpoch,
        bool structures = true,
        bool upgrades = true,
        int leaveQueueSlots = 0,
        int remainingRoom = 64,
        bool roomReadable = true,
        AutoBuyCandidateKinds owned = AutoBuyCandidateKinds.All,
        IAutoBuyRefusalResponsePort? refusals = null,
        AutoBuyPlanBelief belief = default)
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new FakeQueueRoom(remainingRoom, roomReadable),
            () => nativeEpoch,
            () => owned,
            refusals ?? IgnoreRefusals.Instance);
        return adapter.TryExecute(
            new AutoBuyCycleAction(kind, uuid, plannedEpoch, count: 1, belief),
            Config(structures, upgrades, leaveQueueSlots),
            Context());
    }

    /// <summary>
    /// A purchase for a kind another plugin holds the lease for is declined, without mutating.
    /// </summary>
    /// <remarks>
    /// The lease is checked here rather than while deciding because it can move mid-cycle, and
    /// because a copy carried into the frame is an answer that has to be taken again anyway. Auto Buy
    /// carried one and never read it, so until this check existed it purchased for families it had
    /// stood down from. Kinds are checked one at a time: holding structures says nothing about
    /// upgrades.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoBuyCandidateKinds.Upgrades)]
    [InlineData((int)AutoBuyCandidateKinds.None)]
    public void Execute_StructurePurchaseWithoutTheStructureLease_IsDeclined(int owned)
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            owned: (AutoBuyCandidateKinds)owned);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(3, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_UpgradePurchaseWithoutTheUpgradeLease_IsDeclined()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch,
            owned: AutoBuyCandidateKinds.Structures);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(2, upgrade.level);
    }

    private static ServiceActionContext Context(
        int actionIndex = 0,
        long attemptedAt = 1) =>
        new ServiceActionContext(
            new ServiceCycleIdentity(
                new ServiceId("AutoBuy"),
                new LifecycleGeneration((ulong)PlannedEpoch),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(checked((ulong)actionIndex + 1)),
            actionIndex,
            new MonotonicTimestamp(attemptedAt));

    private static AutoBuyPlanBelief Belief(
        Guid resourceId,
        BigDouble cost,
        BigDouble available) =>
        new(
            isAvailable: true,
            hasFiniteLevels: false,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 0,
            queuedLevels: 0,
            costResourceCount: 1,
            pricedResourceCount: 1,
            costRatio: 0.5,
            bindingResourceId: resourceId,
            bindingIsBandwidth: false,
            bindingCost: cost,
            bindingAvailable: available,
            bindingReserveFloor: default);

    private sealed class FixedWorldGeneration : IServiceWorldGenerationSource
    {
        private readonly WorldGeneration _generation;

        internal FixedWorldGeneration(WorldGeneration generation) => _generation = generation;

        public bool TryGetLatestGeneration(out WorldGeneration generation)
        {
            generation = _generation;
            return true;
        }
    }

    private static SuiteRuntimeConfiguration Config(bool structures, bool upgrades, int leaveQueueSlots = 0) =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = structures,
                IncludeUpgrades = upgrades,
                LeaveQueueSlots = leaveQueueSlots,
                EvaluationIntervalSeconds = 0.5f,
            },
        };

    [Fact]
    public void Execute_ReusedAdapter_ResolvesCandidateAddedAfterTheIndexWasBuilt()
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);
        var first = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(first);
        var warmup = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(first.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());
        Assert.Equal(ServiceActionDisposition.Committed, warmup.Disposition);

        var added = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 5,
        };
        global::StructureSO.All.Add(added);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(added.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(6, added.queuedQuantity);
    }

    [Fact]
    public void Execute_ReusedAdapter_SelfHealsWhenTheNativeListIsRebuiltInPlace()
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);
        var original = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(original);
        var warmup = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(original.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());
        Assert.Equal(ServiceActionDisposition.Committed, warmup.Disposition);

        // Same list reference and count, entirely new membership — the stale index must
        // miss and rebuild instead of failing the action.
        global::StructureSO.All.Clear();
        var replacement = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(replacement);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(replacement.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, replacement.queuedQuantity);
    }

    [Fact]
    public void Execute_NativePurchaseThrows_FaultsAdapterWithoutEvidence()
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new ThrowingPurchasePort(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    /// <summary>
    /// One upgrade action is one native call but several queue stacks — the game queues one entry per
    /// committed level and its purchase loop never consults the queue room. Checking that a single slot
    /// exists above the reserve therefore does not mean the whole request fits above it.
    /// </summary>
    [Fact]
    public void Execute_UpgradeMultiBuyLargerThanTheRoomAboveTheReserve_ClampsToIt()
    {
        var purchases = new RecordingPurchasePort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(5, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 4),
            Config(structures: true, upgrades: true, leaveQueueSlots: 3),
            Context());

        Assert.Equal(2, purchases.LastCount);
    }

    [Fact]
    public void Execute_UpgradeMultiBuyThatFitsAboveTheReserve_SubmitsTheFullCount()
    {
        var purchases = new RecordingPurchasePort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(10, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 4),
            Config(structures: true, upgrades: true, leaveQueueSlots: 3),
            Context());

        Assert.Equal(4, purchases.LastCount);
    }

    private sealed class RecordingPurchasePort : IAutoBuyNativePurchasePort
    {
        internal int LastCount { get; private set; }

        public AutoBuyPurchaseSubmission Submit(AutoBuyCandidateKind kind, Guid uuid, int count)
        {
            LastCount = count;
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);
        }
    }

    private sealed class IgnoreRefusals : IAutoBuyRefusalResponsePort
    {
        internal static IgnoreRefusals Instance { get; } = new();
        public void ObserveRefusal(in AutoBuyRefusalReport report)
        {
        }
    }

    private sealed class ThrowingPurchasePort : IAutoBuyNativePurchasePort
    {
        public AutoBuyPurchaseSubmission Submit(AutoBuyCandidateKind kind, Guid uuid, int count) =>
            throw new InvalidOperationException("native purchase threw");
    }

    private sealed class FakeQueueRoom : IAutoBuyQueueRoomPort
    {
        private readonly int _room;
        private readonly bool _readable;

        public FakeQueueRoom(int room, bool readable)
        {
            _room = room;
            _readable = readable;
        }

        public bool TryReadRemainingRoom(out int remainingRoom)
        {
            remainingRoom = _readable ? _room : 0;
            return _readable;
        }
    }

    private static void ResetNativeState()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::GlobalVariables.MultiBuy = new global::IntVariable();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }
}
