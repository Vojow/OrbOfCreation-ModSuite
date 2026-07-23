using System;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestMutationAdapterTests
{
    [Fact]
    public void InitialCaptureFailurePreservesNativeFailureEvidence()
    {
        var adapter = new AutoHarvestMutationAdapter(new ThrowingStatePort());

        var result = adapter.Submit(ResolvedPair());

        Assert.True(result.HasNativeMutationOutcome);
        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, result.NativeMutationOutcome);
        Assert.False(result.MutationAttempted);
        Assert.Equal(AutoHarvestSubmissionFailureCode.None, result.FailureCode);
    }

    [Fact]
    public void FinalPolicyRevalidationReusesTheVerifierBeforeSnapshot()
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair());

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(1, state.ReadCount);
        Assert.Equal(state.Captured, state.FactsActiveState);
        Assert.False(result.MutationAttempted);
    }

    [Fact]
    public void ExactSingleSubmissionTransitionIsVerified()
    {
        var before = State(used: 2, empty: 1, nativeHasEmptyEntry: true, supported: 0, matches: 0, quantity: 0, engaged: false);
        var after = State(used: 3, empty: 0, nativeHasEmptyEntry: false, supported: 1, matches: 1, quantity: 1, engaged: true);

        Assert.True(AutoHarvestMutationAdapter.VerifyTransition(before, after));
    }

    [Theory]
    [InlineData(2, 1, 1, true)]
    [InlineData(1, 2, 1, true)]
    [InlineData(1, 1, 2, true)]
    [InlineData(1, 1, 1, false)]
    public void AmbiguousOrOverSubmittedTransitionIsRejected(
        int supported,
        int matches,
        int quantity,
        bool engaged)
    {
        var before = State(used: 2, empty: 1, nativeHasEmptyEntry: true, supported: 0, matches: 0, quantity: 0, engaged: false);
        var after = State(used: 3, empty: 0, nativeHasEmptyEntry: false, supported, matches, quantity, engaged);

        Assert.False(AutoHarvestMutationAdapter.VerifyTransition(before, after));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SubmissionRequiresBothEnumeratedAndNativeEmptyEntryEvidence(
        int emptyEntries,
        bool nativeHasEmptyEntry)
    {
        var before = State(
            used: 2,
            empty: emptyEntries,
            nativeHasEmptyEntry,
            supported: 0,
            matches: 0,
            quantity: 0,
            engaged: false);
        var after = State(
            used: 3,
            empty: 0,
            nativeHasEmptyEntry: false,
            supported: 1,
            matches: 1,
            quantity: 1,
            engaged: true);

        Assert.False(AutoHarvestMutationAdapter.VerifyTransition(before, after));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void SubmissionRejectsContradictoryPostMutationEmptyEntryEvidence(
        int emptyEntries,
        bool nativeHasEmptyEntry)
    {
        var before = State(
            used: emptyEntries == 0 ? 2 : 1,
            empty: emptyEntries + 1,
            nativeHasEmptyEntry: true,
            supported: 0,
            matches: 0,
            quantity: 0,
            engaged: false);
        var after = State(
            used: emptyEntries == 0 ? 3 : 2,
            empty: emptyEntries,
            nativeHasEmptyEntry,
            supported: 1,
            matches: 1,
            quantity: 1,
            engaged: true);

        Assert.False(AutoHarvestMutationAdapter.VerifyTransition(before, after));
    }

    [Theory]
    [InlineData(false, 1, true, 0, 0, (int)AutoHarvestSubmissionFailureCode.RuntimeReadFailed)]
    [InlineData(true, 0, false, 0, 0, (int)AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected)]
    [InlineData(true, 1, true, 1, 1, (int)AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected)]
    public void FinalInvalidFullOrDuplicateStateDoesNotInvokeNativeMutation(
        bool valid,
        int empty,
        bool nativeHasEmptyEntry,
        int supported,
        int matches,
        int expectedFailure)
    {
        var executed = false;
        var afterCaptured = false;
        var before = new AutoHarvestSubmissionState(
            valid,
            usedEntryCount: 2,
            empty,
            nativeHasEmptyEntry,
            supported,
            matches,
            pairQuantity: matches,
            pairEngaged: matches != 0);

        var result = AutoHarvestMutationAdapter.SubmitCaptured(
            "test-action",
            before,
            () =>
            {
                afterCaptured = true;
                return before;
            },
            () => executed = true);

        Assert.False(executed);
        Assert.False(afterCaptured);
        Assert.False(result.MutationAttempted);
        Assert.Equal((AutoHarvestSubmissionFailureCode)expectedFailure, result.FailureCode);
    }

    private static AutoHarvestSubmissionState State(
        int used,
        int empty,
        bool nativeHasEmptyEntry,
        int supported,
        int matches,
        int quantity,
        bool engaged) =>
        new(
            isValid: true,
            usedEntryCount: used,
            emptyEntryCount: empty,
            nativeHasEmptyEntry,
            supportedCollectCount: supported,
            pairMatchCount: matches,
            pairQuantity: quantity,
            pairEngaged: engaged);

    private static ResolvedAutoHarvestPair ResolvedPair()
    {
        var target = new AutoHarvestPairBinding(
            AutoHarvestPair.FruitTree,
            new object(),
            new object(),
            AutoHarvestKnownIds.FruitTreePlot,
            AutoHarvestKnownIds.FruitTreeCollect,
            new object(),
            null!,
            null!,
            null!,
            growthSeconds: 1,
            restSeconds: 1,
            actionSeconds: 1);
        var shared = new AutoHarvestSharedBinding(new object(), new object(), null!, null!, 1);
        return new ResolvedAutoHarvestPair(null!, shared, target, target, null);
    }

    private sealed class ThrowingStatePort : IAutoHarvestStatePort
    {
        public AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved) =>
            throw new InvalidOperationException("capture failed");

        public AutoHarvestActiveActionSnapshot CaptureActiveActions(
            in ResolvedAutoHarvestPair resolved) =>
            throw new NotSupportedException();

        public void ReadFacts(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestSubmissionState activeState,
            out AutoHarvestPairFacts facts,
            out object? prototype) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStatePort : IAutoHarvestStatePort
    {
        public AutoHarvestSubmissionState Captured { get; } =
            State(used: 2, empty: 1, nativeHasEmptyEntry: true, supported: 0, matches: 0, quantity: 0, engaged: false);

        public int CaptureCount { get; private set; }
        public int ReadCount { get; private set; }
        public AutoHarvestSubmissionState FactsActiveState { get; private set; }

        public AutoHarvestSubmissionState CaptureSubmissionState(
            in ResolvedAutoHarvestPair resolved)
        {
            CaptureCount++;
            return Captured;
        }

        public AutoHarvestActiveActionSnapshot CaptureActiveActions(
            in ResolvedAutoHarvestPair resolved) =>
            throw new NotSupportedException();

        public void ReadFacts(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestSubmissionState activeState,
            out AutoHarvestPairFacts facts,
            out object? prototype)
        {
            ReadCount++;
            FactsActiveState = activeState;
            facts = new AutoHarvestPairFacts(
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Rejected,
                AutoHarvestActionSafetyState.NativePhaseCyclePreserving,
                AutoHarvestEvidenceState.Verified,
                AutoHarvestEvidenceState.Verified);
            prototype = new object();
        }
    }
}
