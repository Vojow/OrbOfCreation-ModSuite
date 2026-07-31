using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsFeatureStatusProjectorTests
{
    [Fact]
    public void TargetFailureRetainsItsExactExplanationInFeatureHealth()
    {
        var health = new AutoItemsActionHealth();
        var submission = AutoItemsSubmission.Reject(
            AutoItemsPreflight.TargetUnavailable,
            "The live Scroll target selector found no valid structure target at level 4.");
        health.Observe(in submission);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Scroll,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            health);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void QuarantineIsAFeatureFaultWithTheExactMutationEvidence()
    {
        var health = new AutoItemsActionHealth();
        var submission = AutoItemsSubmission.Reject(
            AutoItemsPreflight.Quarantined,
            "Auto Items is quarantined for this lifecycle after item abc had no queue delta.");
        health.Observe(in submission);

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.Relic,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            health);

        Assert.Equal(FeatureStatusState.Faulted, status.State);
        Assert.Equal(FeatureStatusReasonCode.MutationQuarantined, status.Reason);
        Assert.Equal(submission.Reason, status.Summary);
    }

    [Fact]
    public void TemporaryFollowUpQuarantineNamesTheExactItemAndCause()
    {
        var itemId = Guid.Parse("00000000-0000-0000-0000-000000000777");

        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.TemporaryItemQuarantined,
            itemId,
            AutoItemsTemporaryQuarantineCause.MultipleUsages,
            new AutoItemsActionHealth());

        Assert.Equal(FeatureStatusState.Faulted, status.State);
        Assert.Equal(FeatureStatusReasonCode.MutationQuarantined, status.Reason);
        Assert.Contains(itemId.ToString("D"), status.Summary);
        Assert.Contains("more than one native usage", status.Summary);
    }

    [Fact]
    public void PublishedNativePreparationIsOperationalWaitingRatherThanAnActionFailure()
    {
        var status = AutoItemsFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            cycleObserved: true,
            AutoItemsDecisionKind.NativePreparationActive,
            Guid.Empty,
            AutoItemsTemporaryQuarantineCause.None,
            new AutoItemsActionHealth());

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
        Assert.Contains("currently preparing", status.Summary);
    }

    [Fact]
    public void IdenticalExpectedRejectionDoesNotCreateNewHealthTransitions()
    {
        var health = new AutoItemsActionHealth();
        var submission = AutoItemsSubmission.Reject(
            AutoItemsPreflight.TargetUnavailable,
            "The live Scroll target selector found no valid structure target.");

        Assert.True(health.Observe(in submission));
        var revision = health.Revision;
        Assert.False(health.Observe(in submission));
        Assert.Equal(revision, health.Revision);

        Assert.True(health.ClearTransient());
        Assert.False(health.HasFailure);
        Assert.False(health.ClearTransient());
    }

    [Fact]
    public void TransientCleanupDoesNotEraseAQuarantine()
    {
        var health = new AutoItemsActionHealth();
        var submission = AutoItemsSubmission.Reject(
            AutoItemsPreflight.Quarantined,
            "Auto Items quarantined an ambiguous native submission.");

        Assert.True(health.Observe(in submission));
        Assert.False(health.ClearTransient());
        Assert.True(health.HasFailure);
        Assert.Equal(AutoItemsPreflight.Quarantined, health.Preflight);
    }
}
