using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeCycleActionAdapterTests
{
    [Fact]
    public void QuarantinedRejectionPreservesOriginalFaultAndDoesNotAdvanceRevision()
    {
        var health = new AutoScribeActionHealth();
        var receipt = new AutoScribeMutationReceipt(
            evidenceAvailable: true,
            paymentInvoked: true,
            resourcesCharged: true,
            costMatched: false,
            ceilingTransitionObserved: true,
            admittedToQueue: true,
            admittedToInstantStock: false,
            queueDelta: 1,
            stockDelta: 0);
        var rootFault = new AutoScribeSubmission(
            AutoScribePreflight.VerificationFailed,
            AutoScribeNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(4, 1, 1),
            in receipt,
            "The exact resource charge did not match.");
        var quarantined = AutoScribeSubmission.Reject(
            AutoScribePreflight.Quarantined,
            "The lifecycle action boundary is quarantined.");

        Assert.True(health.Observe(in rootFault));
        var rootRevision = health.Revision;

        Assert.False(health.Observe(in quarantined));
        Assert.False(health.Observe(in quarantined));

        Assert.True(health.HasFailure);
        Assert.Equal(rootRevision, health.Revision);
        Assert.Equal(AutoScribePreflight.VerificationFailed, health.Preflight);
        Assert.Equal(AutoScribeNativeStage.Verification, health.Stage);
        Assert.Equal("The exact resource charge did not match.", health.Reason);
        Assert.True(health.Receipt.EvidenceAvailable);
        Assert.True(health.Receipt.PaymentInvoked);
        Assert.True(health.Receipt.ResourcesCharged);
        Assert.False(health.Receipt.CostMatched);
        Assert.True(health.Receipt.CeilingTransitionObserved);
        Assert.True(health.Receipt.AdmittedToQueue);
        Assert.False(health.Receipt.AdmittedToInstantStock);
        Assert.Equal(1, health.Receipt.QueueDelta);
        Assert.Equal(0, health.Receipt.StockDelta);
    }

    [Fact]
    public void QuarantinedRejectionWithoutPriorFaultIsRecordedOnce()
    {
        var health = new AutoScribeActionHealth();
        var quarantined = AutoScribeSubmission.Reject(
            AutoScribePreflight.Quarantined,
            "The lifecycle action boundary is quarantined.");

        Assert.True(health.Observe(in quarantined));
        Assert.False(health.Observe(in quarantined));

        Assert.Equal(1, health.Revision);
        Assert.Equal(AutoScribePreflight.Quarantined, health.Preflight);
        Assert.Equal("The lifecycle action boundary is quarantined.", health.Reason);
    }

    [Fact]
    public void VerifiedSubmissionClearsPreservedFaultAndAdvancesRevisionOnce()
    {
        var health = new AutoScribeActionHealth();
        var failed = AutoScribeSubmission.Reject(
            AutoScribePreflight.ContractUnavailable,
            "The native binding set is unavailable.");
        var receipt = new AutoScribeMutationReceipt(
            evidenceAvailable: true,
            paymentInvoked: true,
            resourcesCharged: true,
            costMatched: true,
            ceilingTransitionObserved: true,
            admittedToQueue: true,
            admittedToInstantStock: false,
            queueDelta: 1,
            stockDelta: 0);
        var verified = new AutoScribeSubmission(
            AutoScribePreflight.Proceeded,
            AutoScribeNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(4, 1, 1),
            in receipt,
            string.Empty);

        Assert.True(health.Observe(in failed));
        Assert.False(health.Observe(in verified));

        Assert.False(health.HasFailure);
        Assert.Equal(2, health.Revision);
        Assert.Equal(AutoScribePreflight.Proceeded, health.Preflight);
        Assert.Equal(AutoScribeNativeStage.None, health.Stage);
        Assert.Equal(string.Empty, health.Reason);
    }

    [Fact]
    public void LifecycleInvalidationClearsPreservedFault()
    {
        var health = new AutoScribeActionHealth();
        var failed = AutoScribeSubmission.Reject(
            AutoScribePreflight.PostPaymentFault,
            "Construction failed after payment.");
        var quarantined = AutoScribeSubmission.Reject(
            AutoScribePreflight.Quarantined,
            "The lifecycle action boundary is quarantined.");
        Assert.True(health.Observe(in failed));
        Assert.False(health.Observe(in quarantined));

        health.InvalidateLifecycle();

        Assert.False(health.HasFailure);
        Assert.Equal(2, health.Revision);
        Assert.Equal(AutoScribePreflight.Proceeded, health.Preflight);
        Assert.Equal(AutoScribeNativeStage.None, health.Stage);
        Assert.Equal(string.Empty, health.Reason);
    }

    [Fact]
    public void PaidPartialCommitMapsToFaultedEvidenceWithItsExactCallShape()
    {
        var receipt = new AutoScribeMutationReceipt(
            evidenceAvailable: true,
            paymentInvoked: true,
            resourcesCharged: true,
            costMatched: true,
            ceilingTransitionObserved: true,
            admittedToQueue: false,
            admittedToInstantStock: false,
            queueDelta: 0,
            stockDelta: 0);
        var submission = new AutoScribeSubmission(
            AutoScribePreflight.PostPaymentFault,
            AutoScribeNativeStage.Construction,
            NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(2, 1, 0),
            in receipt,
            "Construction failed after exact payment.");

        var result = AutoScribeCycleActionAdapter.Map(in submission);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.ExecutionThrew, result.NativeEvidence.Outcome);
        Assert.Equal(2, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void OrdinaryPreflightRejectionCarriesNoMutationEvidence()
    {
        var submission = AutoScribeSubmission.Reject(
            AutoScribePreflight.QueueFull,
            "ActiveScribeInstances.HasEmptySpot() refused before payment.");

        var result = AutoScribeCycleActionAdapter.Map(in submission);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoScribeActionResultCodes.QueueFull, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    [Fact]
    public void OpenPublicationGapRejectsAutoScribeBeforeNativeResolution()
    {
        IDictionary registry = new Dictionary<Guid, object>();
        using var gameAction = new AutoScribeOneShotCraftGameAction(
            new TypedRegistryResolver(
                static () => 7,
                () => TypedRegistrySourceSnapshot.Ready(registry),
                static _ => null),
            AutoScribeIdentityCatalog.Audited,
            static () => true,
            static () => string.Empty,
            static () => 20,
            static (_, _) => { });
        var gap = new ConsumableMutationPublicationGapCoordinator();
        gap.ObserveMutationAttempt(7, 19);
        var adapter = new AutoScribeCycleActionAdapter(
            gameAction,
            static () => 7,
            static () => true,
            static () => string.Empty,
            new AutoScribeActionHealth(),
            gap);
        var action = new AutoScribeCycleAction(Guid.NewGuid(), Guid.NewGuid(), 1, 7);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
            },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
        };
        var context = new ServiceActionContext(
            new ServiceCycleIdentity(
                AutoScribeServicePolicies.ServiceId,
                new LifecycleGeneration(7),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(1));

        var result = adapter.TryExecute(in action, in config, in context);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoScribeActionResultCodes.PublicationGap, result.Code);
        Assert.False(result.HasNativeEvidence);
    }
}
