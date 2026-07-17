using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyRejectionTelemetryTests
{
    [Fact]
    public void ReservePolicy_CapturesEveryBlockingResource()
    {
        var config = AutomataConfig.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;
        var decision = new ReservePolicy(config).Evaluate(new[]
        {
            Cost("mana", 10, 5),
            Cost("knowledge", 20, 10),
        });

        Assert.False(decision.Passed);
        Assert.Equal(ReserveDecisionFailure.ReserveViolation, decision.Failure);
        Assert.Collection(
            decision.ResourceBlockers,
            blocker =>
            {
                Assert.Equal("mana", blocker.ResourceId);
                Assert.Equal(AutoBuyResourceBlockerKind.ReserveFloor, blocker.Kind);
                Assert.Equal(0, blocker.RequiredQuantity.CompareTo(new BigAmount(10, 0)));
            },
            blocker =>
            {
                Assert.Equal("knowledge", blocker.ResourceId);
                Assert.Equal(AutoBuyResourceBlockerKind.ReserveFloor, blocker.Kind);
                Assert.Equal(0, blocker.RequiredQuantity.CompareTo(new BigAmount(20, 0)));
            });
    }

    [Fact]
    public void AffordabilityBlockers_ContainOnlyResourcesAboveTheConfiguredRatio()
    {
        var blockers = AutoBuyEngine.BuildAffordabilityBlockers(
            new[]
            {
                Cost("mana", 10, 50),
                Cost("knowledge", 2, 100),
            },
            maximumRatio: 0.1);

        var blocker = Assert.Single(blockers);
        Assert.Equal("mana", blocker.ResourceId);
        Assert.Equal(AutoBuyResourceBlockerKind.AffordabilityThreshold, blocker.Kind);
        Assert.Equal(0, blocker.AvailableQuantity.CompareTo(new BigAmount(50, 0)));
        Assert.Equal(0, blocker.RequiredQuantity.CompareTo(new BigAmount(100, 0)));
    }

    [Fact]
    public void Telemetry_DistinguishesUnchangedResourceWaitsFromThresholdChanges()
    {
        var candidate = new FakeCandidate("candidate");
        var snapshot = candidate.Snapshot();
        var telemetry = new AutoBuyRejectionTelemetry();

        telemetry.Record(ResourceRejection(snapshot, available: 50, required: 100));
        telemetry.Record(ResourceRejection(snapshot, available: 60, required: 100));
        telemetry.Record(ResourceRejection(snapshot, available: 60, required: 120));
        telemetry.Record(AutoBuyDecision.Recommended(snapshot, 0.05, "ready"));

        var result = telemetry.Snapshot();
        Assert.Equal(4, result.Evaluations);
        Assert.Equal(1, result.Recommendations);
        Assert.Equal(3, result.Rejections);
        Assert.Equal(1, result.RepeatedUnchangedRejections);
        Assert.Equal(2, result.RejectionStateChanges);
        Assert.Equal(1, result.RejectionExits);
        Assert.Equal(0, result.CurrentRejectedCandidates);
        Assert.Equal(3, result.RejectionsByReason[AutoBuyRejectionReason.AffordabilityThreshold]);
        Assert.Equal("AffordabilityThreshold=3", result.FormatReasonCounts());
    }

    [Fact]
    public void Telemetry_TreatsDifferentNativeReasonsAsDifferentStates()
    {
        var candidate = new FakeCandidate("native");
        var snapshot = candidate.Snapshot();
        var telemetry = new AutoBuyRejectionTelemetry();

        telemetry.Record(AutoBuyDecision.Rejected(
            snapshot,
            AutoBuyRejectionReason.NativeNotPurchasable,
            "prerequisite missing"));
        telemetry.Record(AutoBuyDecision.Rejected(
            snapshot,
            AutoBuyRejectionReason.NativeNotPurchasable,
            "already queued"));

        var result = telemetry.Snapshot();
        Assert.Equal(0, result.RepeatedUnchangedRejections);
        Assert.Equal(2, result.RejectionStateChanges);
        Assert.Equal(1, result.CurrentRejectedCandidates);
    }

    [Fact]
    public void Telemetry_TracksScanLimitAsDeferralInsteadOfEvaluationRejection()
    {
        var candidate = new FakeCandidate("excluded-by-scan-limit");
        var telemetry = new AutoBuyRejectionTelemetry();

        telemetry.Record(AutoBuyDecision.Rejected(
            candidate.Snapshot(),
            AutoBuyRejectionReason.CandidateScanLimit,
            "candidate scan limit reached"));

        var result = telemetry.Snapshot();
        Assert.Equal(0, result.Evaluations);
        Assert.Equal(0, result.Rejections);
        Assert.Equal(0, result.RejectionStateChanges);
        Assert.Equal(0, result.CurrentRejectedCandidates);
        Assert.Equal(1, result.ScanLimitDeferrals);
        Assert.Empty(result.RejectionsByReason);
    }

    private static ResourceAdmissionCost Cost(string resourceId, double cost, double available)
    {
        return new ResourceAdmissionCost(
            resourceId,
            resourceId,
            new BigAmount(cost, 0),
            new BigAmount(available, 0));
    }

    private static AutoBuyDecision ResourceRejection(
        AutoBuyCandidateSnapshot snapshot,
        double available,
        double required)
    {
        return AutoBuyDecision.Rejected(
            snapshot,
            AutoBuyRejectionReason.AffordabilityThreshold,
            "resource threshold not met",
            new[]
            {
                new AutoBuyResourceBlocker(
                    AutoBuyResourceBlockerKind.AffordabilityThreshold,
                    "mana",
                    "Mana",
                    new BigAmount(10, 0),
                    new BigAmount(available, 0),
                    new BigAmount(required, 0)),
            });
    }

    private sealed class FakeCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public FakeCandidate(string uuid)
        {
            _snapshot = new AutoBuyCandidateSnapshot(
                this,
                uuid,
                uuid,
                AutoBuyCandidateKind.Structure,
                nameof(FakeCandidate));
        }

        public AutoBuyCandidateSnapshot Snapshot() => _snapshot;

        public bool IsAvailable() => true;

        public bool CanPurchase(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<ResourceAdmissionCost> GetCosts() => Array.Empty<ResourceAdmissionCost>();

        public bool TryPurchaseOne(out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }
}
