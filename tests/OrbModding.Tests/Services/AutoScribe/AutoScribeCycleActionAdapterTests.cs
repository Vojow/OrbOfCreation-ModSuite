using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeCycleActionAdapterTests
{
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
}
