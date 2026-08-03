using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeCycleActionAdapterTests
{
    [Fact]
    public void ActionFamilyContentionStopsBeforeTheWorkerCanPlanActions()
    {
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
            },
        };
        var context = default(ServiceCycleStartContext);

        var decision = AutoScribeService.ShouldStart(
            in configuration,
            in context,
            ownsActionFamily: static () => false);

        Assert.False(decision.ShouldStart);
        Assert.Equal(WakePolicy.OnPublication, decision.WakePolicy);
    }

    [Fact]
    public void PostPaymentFaultMapsToFaultedOutcomeWithItsExactCallShape()
    {
        var submission = new AutoScribeSubmission(
            AutoScribePreflight.PostPaymentFault,
            AutoScribeNativeStage.Construction,
            NativeMutationOutcome.ExecutionThrew,
            new NativeMutationCallOutcome(2, 1, 0),
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
    public void BackpressureAndFailuresHaveOneSharedClassification()
    {
        var health = new AutoScribeActionHealth();
        var registryWait = AutoScribeSubmission.Reject(
            AutoScribePreflight.IdentityUnavailable,
            "The lifecycle registry is not ready.");
        var permitWait = AutoScribeSubmission.Reject(
            AutoScribePreflight.MutationPermitUnavailable,
            "CraftingQueueSubmission is busy.");
        var contradiction = AutoScribeSubmission.Reject(
            AutoScribePreflight.RelationshipMismatch,
            "The live recipe relation contradicted the audited role.");

        Assert.Equal(
            ServiceActionDisposition.Rejected,
            AutoScribeCycleActionAdapter.Map(in registryWait).Disposition);
        Assert.False(health.Observe(in registryWait));
        Assert.Equal(
            ServiceActionDisposition.Rejected,
            AutoScribeCycleActionAdapter.Map(in permitWait).Disposition);
        Assert.False(health.Observe(in permitWait));
        Assert.False(health.HasFailure);

        Assert.Equal(
            ServiceActionDisposition.Faulted,
            AutoScribeCycleActionAdapter.Map(in contradiction).Disposition);
        Assert.True(health.Observe(in contradiction));
        Assert.True(health.HasFailure);
    }

    [Fact]
    public void PersistentQuarantineDoesNotReenterTheWarningState()
    {
        const string reason = "Exact queue admission was not observed after payment.";
        var failure = new AutoScribeSubmission(
            AutoScribePreflight.VerificationFailed,
            AutoScribeNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(4, 1, 0),
            reason);
        var quarantine = AutoScribeSubmission.Reject(
            AutoScribePreflight.Quarantined,
            reason);
        var health = new AutoScribeActionHealth();

        Assert.True(health.Observe(in failure));
        var failureRevision = health.Revision;
        Assert.False(health.Observe(in quarantine));

        Assert.Equal(failureRevision, health.Revision);
        Assert.Equal(AutoScribePreflight.VerificationFailed, health.Preflight);
        Assert.Equal(ServiceActionDisposition.Faulted,
            AutoScribeCycleActionAdapter.Map(in quarantine).Disposition);
    }
}
