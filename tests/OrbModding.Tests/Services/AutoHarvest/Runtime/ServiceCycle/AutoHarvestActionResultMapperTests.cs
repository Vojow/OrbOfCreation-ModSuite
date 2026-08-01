using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestActionResultMapperTests
{
    [Theory]
    [InlineData(NativeMutationOutcome.Verified, 1, 1, 1, ServiceActionDisposition.Committed)]
    [InlineData(NativeMutationOutcome.BeforeCaptureFailed, 0, 0, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.ExecutionThrew, 1, 1, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.AfterCaptureFailed, 1, 1, 0, ServiceActionDisposition.Faulted)]
    [InlineData(NativeMutationOutcome.PostconditionFailed, 1, 1, 0, ServiceActionDisposition.Faulted)]
    public void FrozenLegacyNativeEvidenceMatrixMatchesServiceCycle(
        NativeMutationOutcome outcome,
        int calls,
        int attempts,
        int commits,
        ServiceActionDisposition expectedDisposition)
    {
        var mutation = new AutoHarvestSubmissionResult(
            outcome,
            new NativeMutationCallOutcome(calls, attempts, commits));

        var result = AutoHarvestActionResultMapper.FromMutation(mutation);

        Assert.Equal(expectedDisposition, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(outcome, result.NativeEvidence.Outcome);
        Assert.Equal(calls, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(attempts, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(commits, result.NativeCallOutcome.MutationsCommitted);
    }

    [Theory]
    [InlineData(6, 7UL)]
    [InlineData(7, 6UL)]
    [InlineData(long.MaxValue, 9223372036854775808UL)]
    public void NativeLifecycleMustExactlyMatchTheCaptureOrActionCycle(
        long nativeLifecycle,
        ulong plannedLifecycle)
    {
        Assert.False(AutoHarvestNativeLifecycle.Matches(
            nativeLifecycle,
            new LifecycleGeneration(plannedLifecycle)));
    }

    [Fact]
    public void PositiveMatchingLifecycleIsAccepted()
    {
        Assert.True(AutoHarvestNativeLifecycle.Matches(
            7,
            new LifecycleGeneration(7)));
    }

    [Theory]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativePrerequisitesCurrentlyUnmet,
        1028)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativePrerequisiteValidationUnavailable,
        1029)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativePairIdentityRevalidationRefused,
        1030)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativePlotVisibilityRefused,
        1031)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativeOfferedInstanceMembershipRefused,
        1032)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativeActionRowVisibilityRefused,
        1033)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativeHasEnoughForOneInstanceRefused,
        1034)]
    [InlineData(
        (int)AutoHarvestSubmissionFailureCode.NativeMaximumRemainingInstancesRefused,
        1035)]
    public void AdmissionRefusalsArePenaltyFreeAndKeepTheirExactCode(
        int failure,
        int expectedCode)
    {
        var mutation = new AutoHarvestSubmissionResult(
            (AutoHarvestSubmissionFailureCode)failure);

        var result = AutoHarvestActionResultMapper.FromMutation(mutation);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(new ServiceActionResultCode(expectedCode), result.Code);
        Assert.False(result.HasNativeEvidence);
    }
}
