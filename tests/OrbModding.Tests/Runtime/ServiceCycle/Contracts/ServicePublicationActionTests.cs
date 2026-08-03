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

}
