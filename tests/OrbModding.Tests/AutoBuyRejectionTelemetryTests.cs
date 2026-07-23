using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoBuyRejectionTelemetryTests
{
    [Fact]
    public void ReservePolicy_CapturesEveryBlockingResource()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
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
    public void ReservePolicy_DescribesZeroReserveFailureAsInsufficientCostCoverage()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AbsoluteReserve.Value = "0";
        config.RelativeReserveMultiplier.Value = 0.0f;

        var decision = new ReservePolicy(config).Evaluate(new[] { Cost("mana", 10, 5) });

        Assert.False(decision.Passed);
        Assert.Equal("insufficient mana: have 5e0, need 1e1 to cover cost", decision.Reason);
        Assert.DoesNotContain("including reserve", decision.Reason, StringComparison.Ordinal);
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
        Assert.Equal(3, result.RejectionsByCode[AutomationDecisionCode.AffordabilityThreshold]);
        Assert.Equal("AffordabilityThreshold=3", result.FormatCodeCounts());
    }

    [Fact]
    public void Telemetry_ReportsOnlyNewOrChangedRejectionsForVerboseLogging()
    {
        var snapshot = new FakeCandidate("candidate").Snapshot();
        var telemetry = new AutoBuyRejectionTelemetry();

        Assert.True(telemetry.Record(ResourceRejection(snapshot, available: 50, required: 100)));
        Assert.False(telemetry.Record(ResourceRejection(snapshot, available: 75, required: 100)));
        Assert.True(telemetry.Record(ResourceRejection(snapshot, available: 75, required: 120)));
    }

    [Fact]
    public void Telemetry_ExcludesTechnicalDetailFromTheStructuredConditionKey()
    {
        var candidate = new FakeCandidate("native");
        var snapshot = candidate.Snapshot();
        var telemetry = new AutoBuyRejectionTelemetry();

        telemetry.Record(AutoBuyDecision.Rejected(
            snapshot,
            AutomationDecisionCode.NativeAdmissionRejected,
            "prerequisite missing",
            AutomationRetryTrigger.Lifecycle));
        telemetry.Record(AutoBuyDecision.Rejected(
            snapshot,
            AutomationDecisionCode.NativeAdmissionRejected,
            "already queued",
            AutomationRetryTrigger.Lifecycle));

        var result = telemetry.Snapshot();
        Assert.Equal(1, result.RepeatedUnchangedRejections);
        Assert.Equal(1, result.RejectionStateChanges);
        Assert.Equal(1, result.CurrentRejectedCandidates);
    }

    [Fact]
    public void Telemetry_TracksScanLimitAsDeferralInsteadOfEvaluationRejection()
    {
        var candidate = new FakeCandidate("excluded-by-scan-limit");
        var telemetry = new AutoBuyRejectionTelemetry();

        telemetry.Record(AutoBuyDecision.Rejected(
            candidate.Snapshot(),
            AutomationDecisionCode.ScanLimitDeferred,
            "candidate scan limit reached",
            AutomationRetryTrigger.SchedulerTurn,
            disposition: AutomationDecisionDisposition.Deferred));

        var result = telemetry.Snapshot();
        Assert.Equal(0, result.Evaluations);
        Assert.Equal(0, result.Rejections);
        Assert.Equal(0, result.RejectionStateChanges);
        Assert.Equal(0, result.CurrentRejectedCandidates);
        Assert.Equal(1, result.ScanLimitDeferrals);
        Assert.Empty(result.RejectionsByCode);
    }

    [Fact]
    public void Telemetry_NormalizesBlockerOrderAndIgnoresObservedQuantity()
    {
        var snapshot = new FakeCandidate("multi-resource", "Original candidate name").Snapshot();
        var renamedSnapshot = new FakeCandidate("multi-resource", "Renamed candidate").Snapshot();
        var telemetry = new AutoBuyRejectionTelemetry();

        Assert.True(telemetry.Record(ResourceRejection(
            snapshot,
            new AutoBuyResourceBlocker(
                AutoBuyResourceBlockerKind.AffordabilityThreshold,
                "mana",
                "Mana",
                new BigAmount(10, 0),
                new BigAmount(50, 0),
                new BigAmount(100, 0)),
            new AutoBuyResourceBlocker(
                AutoBuyResourceBlockerKind.AffordabilityThreshold,
                "knowledge",
                "Knowledge",
                new BigAmount(5, 0),
                new BigAmount(20, 0),
                new BigAmount(50, 0)))));
        Assert.False(telemetry.Record(ResourceRejection(
            renamedSnapshot,
            new AutoBuyResourceBlocker(
                AutoBuyResourceBlockerKind.AffordabilityThreshold,
                "knowledge",
                "Renamed Knowledge",
                new BigAmount(5, 0),
                new BigAmount(30, 0),
                new BigAmount(50, 0)),
            new AutoBuyResourceBlocker(
                AutoBuyResourceBlockerKind.AffordabilityThreshold,
                "mana",
                "Renamed Mana",
                new BigAmount(10, 0),
                new BigAmount(75, 0),
                new BigAmount(100, 0)))));

        var result = telemetry.Snapshot();
        Assert.Equal(1, result.RepeatedUnchangedRejections);
        Assert.Equal(1, result.RejectionStateChanges);
    }

    [Fact]
    public void ToggleControl_ExposesLatestStructuredDecisionAndConfigurationStatus()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        var latest = ResourceRejection(
            new FakeCandidate("tooltip-candidate").Snapshot(),
            available: 50,
            required: 100).StructuredDecision;
        var control = new AutoBuyToggleControl(
            config,
            readLatestDecision: () => latest);

        Assert.Equal(latest.ConditionKey, control.LatestDecision?.ConditionKey);
        Assert.Equal(
            AutomationDecisionPresenter.Format(latest),
            AutomationDecisionPresenter.Format(control.LatestDecision!.Value));

        config.EmergencyDisable.Value = true;

        Assert.Equal(AutomationDecisionCode.ConfigurationDisabled, control.LatestDecision?.Code);
        Assert.Contains(
            "Disabled by configuration",
            AutomationDecisionPresenter.Format(control.LatestDecision!.Value),
            StringComparison.Ordinal);
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
            AutomationDecisionCode.AffordabilityThreshold,
            "resource threshold not met",
            AutomationRetryTrigger.ResourceQuantity,
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

    private static AutoBuyDecision ResourceRejection(
        AutoBuyCandidateSnapshot snapshot,
        params AutoBuyResourceBlocker[] blockers)
    {
        return AutoBuyDecision.Rejected(
            snapshot,
            AutomationDecisionCode.AffordabilityThreshold,
            "resource threshold not met",
            AutomationRetryTrigger.ResourceQuantity,
            blockers);
    }

    private sealed class FakeCandidate : IAutoBuyCandidate
    {
        private readonly AutoBuyCandidateSnapshot _snapshot;

        public FakeCandidate(string uuid, string? displayName = null)
        {
            _snapshot = new AutoBuyCandidateSnapshot(
                this,
                uuid,
                displayName ?? uuid,
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
