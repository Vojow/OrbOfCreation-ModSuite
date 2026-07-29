using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Contracts;

/// <summary>
/// An action may commit by publishing a snapshot instead of mutating the game. These pin the part
/// that makes that safe: publishing earns its own evidence and reports no native calls, and the
/// proof obligation on an action that does touch the game is untouched.
/// </summary>
public sealed class ServicePublicationActionTests
{
    private static readonly ServiceCycleIdentity Cycle = new(
        new ServiceId("orbmodding.test"),
        new LifecycleGeneration(1),
        new ConfigGeneration(1),
        new StrategyGeneration(1),
        new WorldGeneration(1),
        new CycleId(1));

    private static ServiceNativeMutationEvidence Verified =>
        ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));

    [Fact]
    public void APublishingCommitCarriesItsGenerationAndReportsNoNativeCalls()
    {
        var result = ServiceActionResult.CommittedPublication(
            CommonActionResultCodes.Committed,
            ServicePublicationEvidence.World(new WorldGeneration(4096)));

        Assert.True(result.IsValid);
        Assert.Equal(ServiceActionEffect.Publication, result.Effect);
        Assert.Equal(ServicePublicationChannel.World, result.PublicationEvidence.Channel);
        Assert.Equal(4096ul, result.PublicationEvidence.Generation);

        // The whole point of not fabricating evidence: the native-call budget stays truthful.
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(0, result.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(0, result.NativeCallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void AMutatingCommitStillHasToProveTheMutation()
    {
        Assert.Equal(ServiceActionEffect.NativeMutation,
            ServiceActionResult.Committed(CommonActionResultCodes.Committed, Verified).Effect);

        Assert.Throws<ArgumentException>(() => ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0))));
    }

    [Fact]
    public void APublishingCommitWithoutAGenerationIsRefused() =>
        Assert.Throws<ArgumentException>(() => ServiceActionResult.CommittedPublication(
            CommonActionResultCodes.Committed,
            default));

    [Fact]
    public void AGenerationOfZeroIsNotEvidence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServicePublicationEvidence.World(new WorldGeneration(0)));
        Assert.Throws<ArgumentException>(() =>
            ServicePublicationEvidence.Strategy(new StrategyGeneration(0)));
    }

    [Fact]
    public void ABatchOfPublicationsOwesNoNativeEvidence()
    {
        var receipt = BatchReceipt.Completed(
            Cycle,
            new BatchId(1),
            actionCount: 1,
            committedCount: 1,
            default,
            new MonotonicTimestamp(10),
            publishedCount: 1);

        Assert.Equal(1, receipt.PublishedCount);
        Assert.Equal(0, receipt.NativeActionCount);
    }

    /// <summary>
    /// The count defaults to zero, so a batch that publishes but forgets to say so is rejected rather
    /// than admitted. A wrong default has to fail closed or it is not a check.
    /// </summary>
    [Fact]
    public void APublishingBatchThatDoesNotDeclareItselfIsRefused() =>
        Assert.Throws<ArgumentException>(() => BatchReceipt.Completed(
            Cycle,
            new BatchId(1),
            actionCount: 1,
            committedCount: 1,
            default,
            new MonotonicTimestamp(10)));

    [Fact]
    public void AMixedBatchStillOwesEvidenceForItsMutatingHalf()
    {
        var receipt = BatchReceipt.Completed(
            Cycle,
            new BatchId(1),
            actionCount: 2,
            committedCount: 2,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(10),
            publishedCount: 1);

        Assert.Equal(1, receipt.NativeActionCount);

        Assert.Throws<ArgumentException>(() => BatchReceipt.Completed(
            Cycle,
            new BatchId(1),
            actionCount: 2,
            committedCount: 2,
            default,
            new MonotonicTimestamp(10),
            publishedCount: 1));
    }

    [Fact]
    public void MorePublicationsThanCommitsIsIncoherent() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchReceipt.Completed(
            Cycle,
            new BatchId(1),
            actionCount: 1,
            committedCount: 1,
            default,
            new MonotonicTimestamp(10),
            publishedCount: 2));
}
