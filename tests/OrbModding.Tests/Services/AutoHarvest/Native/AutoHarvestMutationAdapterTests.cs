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

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.True(result.HasNativeMutationOutcome);
        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, result.NativeMutationOutcome);
        Assert.False(result.MutationAttempted);
        Assert.Equal(AutoHarvestSubmissionFailureCode.None, result.FailureCode);
    }

    /// <summary>
    /// The facts the boundary judges are the ones the action carries; the reader is asked only for
    /// the instance to submit into.
    /// </summary>
    /// <remarks>
    /// A pair the world said was not ready is refused without a second opinion. The old boundary
    /// re-derived these five off the live game — six reads including one that wrote, through
    /// <c>PlotNodeActionInstance.IsVisible()</c> reaching <c>Prerequisites.Container.Check()</c>.
    /// </remarks>
    [Fact]
    public void CarriedFactsAreWhatTheSubmissionDecisionJudges()
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(),
            ReadyFacts(readiness: AutoHarvestEvidenceState.Rejected),
            Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.Equal(1, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
    }

    /// <summary>
    /// An action carrying no facts submits nothing.
    /// </summary>
    /// <remarks>
    /// The evidence states default to unknown, and unknown is a rejection rather than a pass — so an
    /// action that reached the boundary without a plan behind it cannot mutate the game.
    /// </remarks>
    [Fact]
    public void AnActionWithoutFactsIsRefused()
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), default, Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.False(result.MutationAttempted);
    }

    /// <summary>
    /// A plot whose one instance of the action cannot be resolved is refused, however good the facts.
    /// </summary>
    [Fact]
    public void AMissingPrototypeIsRefusedEvenWhenEveryFactAgrees()
    {
        var state = new RecordingStatePort(resolvesPrototype: false);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.PrototypeCount);
        Assert.False(result.MutationAttempted);
    }

    /// <summary>
    /// The queue snapshot the transition verifier will compare against is also the one the policy
    /// judges, and it is captured once.
    /// </summary>
    /// <remarks>
    /// Every world fact says go here; only the live queue says the pair is already running. An
    /// adapter that judged without it would sail past the policy and reach the native submission,
    /// which this fixture cannot perform — so the rejection is the evidence.
    /// </remarks>
    [Fact]
    public void TheLiveQueueIsWhatTheFinalRevalidationAddsToTheWorldFacts()
    {
        var state = new RecordingStatePort(supported: 1);
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(ResolvedPair(), ReadyFacts(), Preserving);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
        Assert.Equal(1, state.CaptureCount);
        Assert.False(result.MutationAttempted);
    }

    /// <summary>
    /// The audited safety the adapter judges is the one the action carried, not an assumed one.
    /// </summary>
    /// <remarks>
    /// The verdict is drawn on the worker, from the snapshot the plan was made against, and travels
    /// with the action. An adapter that supplied a constant instead would pass every test about the
    /// ordinary path and submit into an action the audit rejected.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoHarvestActionSafetyState.Unknown)]
    [InlineData((int)AutoHarvestActionSafetyState.Destructive)]
    [InlineData((int)AutoHarvestActionSafetyState.ResourceDrain)]
    [InlineData((int)AutoHarvestActionSafetyState.UnsafeCompletionEffects)]
    public void AnUnauditedActionIsRejectedOnTheSafetyItCarried(int safetyValue)
    {
        var state = new RecordingStatePort();
        var adapter = new AutoHarvestMutationAdapter(state);

        var result = adapter.Submit(
            ResolvedPair(),
            ReadyFacts(),
            (AutoHarvestActionSafetyState)safetyValue);

        Assert.Equal(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected, result.FailureCode);
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

    private static AutoHarvestPairFacts ReadyFacts(
        AutoHarvestEvidenceState readiness = AutoHarvestEvidenceState.Verified) =>
        new(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            AutoHarvestEvidenceState.Verified,
            readiness);

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

    private const AutoHarvestActionSafetyState Preserving =
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving;

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
            null!);
        var shared = new AutoHarvestSharedBinding(new object(), null!, null!, 1);
        return new ResolvedAutoHarvestPair(null!, shared, target, target, null);
    }

    private sealed class ThrowingStatePort : IAutoHarvestSubmissionStatePort
    {
        public AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved) =>
            throw new InvalidOperationException("capture failed");

        public object? ReadPrototype(in ResolvedAutoHarvestPair resolved) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStatePort : IAutoHarvestSubmissionStatePort
    {
        private readonly object? _prototype;

        internal RecordingStatePort(int supported = 0, bool resolvesPrototype = true)
        {
            _prototype = resolvesPrototype ? new object() : null;
            Captured = State(
                used: 2,
                empty: 1,
                nativeHasEmptyEntry: true,
                supported,
                matches: 0,
                quantity: 0,
                engaged: false);
        }

        public AutoHarvestSubmissionState Captured { get; }

        public int CaptureCount { get; private set; }
        public int PrototypeCount { get; private set; }

        public AutoHarvestSubmissionState CaptureSubmissionState(
            in ResolvedAutoHarvestPair resolved)
        {
            CaptureCount++;
            return Captured;
        }

        public object? ReadPrototype(in ResolvedAutoHarvestPair resolved)
        {
            PrototypeCount++;
            return _prototype;
        }
    }
}
