using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

public sealed class ActionQueueIntegrityTrackerTests
{
    private static readonly Guid QueueId = new("2c4825f4-9869-41fc-84df-301638a097a5");
    private static readonly Guid MemberId = new("7c04fa78-66de-4941-8f7f-e7964f841031");

    [Fact]
    public void StructureExcessIsRecoverableButMatchingCountsAreClean()
    {
        var excess = ActionQueueIntegrityClassifier.Classify(
            Observation(generation: 10, stacks: 5, pending: 4));
        var clean = ActionQueueIntegrityClassifier.Classify(
            Observation(generation: 11, stacks: 4, pending: 4));

        Assert.Equal(ActionQueueIntegrityVerdict.RecoverableExcess, excess.Verdict);
        Assert.Equal(1, excess.ExcessStacks);
        Assert.True(excess.CanIssueRecoveryTicket);
        Assert.Equal(ActionQueueIntegrityVerdict.Clean, clean.Verdict);
        Assert.False(clean.CanIssueRecoveryTicket);
    }

    [Fact]
    public void PostRestartUpgradeExcessStaysAmbiguousAndNeverIssuesATicket()
    {
        var tracker = new ActionQueueIntegrityTracker();
        var first = tracker.Observe(Observation(
            generation: 20,
            stacks: 1,
            pending: 0,
            exactType: ActionQueueIntegrityClassifier.UpgradeNativeType,
            afterRestart: true));
        var second = tracker.Observe(Observation(
            generation: 21,
            stacks: 1,
            pending: 0,
            exactType: ActionQueueIntegrityClassifier.UpgradeNativeType,
            afterRestart: true));

        Assert.Equal(
            ActionQueueIntegrityVerdict.PostRestartUpgradeAmbiguous,
            first.Finding.Verdict);
        Assert.Equal(1, first.Finding.ExcessStacks);
        Assert.Equal(0, first.StableObservations);
        Assert.Null(first.Ticket);
        Assert.Null(second.Ticket);
    }

    [Theory]
    [InlineData("SpellRecipeSO", 1, 0, (int)ActionQueueIntegrityVerdict.UnsupportedNativeType)]
    [InlineData("StructureSO", 1, 2, (int)ActionQueueIntegrityVerdict.AuthoritativePendingExceedsMemberStacks)]
    [InlineData("StructureSO", -1, 0, (int)ActionQueueIntegrityVerdict.InvalidEvidence)]
    public void UnsupportedContradictoryAndInvalidEvidenceFailClosed(
        string exactType,
        int stacks,
        int pending,
        int expected)
    {
        var finding = ActionQueueIntegrityClassifier.Classify(
            Observation(30, stacks, pending, exactType));

        Assert.Equal((ActionQueueIntegrityVerdict)expected, finding.Verdict);
        Assert.False(finding.CanIssueRecoveryTicket);
    }

    [Fact]
    public void TicketRequiresTwoStrictlyNewerSameLifecyclePublications()
    {
        var tracker = new ActionQueueIntegrityTracker();

        var first = tracker.Observe(Observation(40, stacks: 1, pending: 0));
        var duplicateGeneration = tracker.Observe(Observation(40, stacks: 1, pending: 0));
        var second = tracker.Observe(Observation(41, stacks: 1, pending: 0));

        Assert.Equal(1, first.StableObservations);
        Assert.Null(first.Ticket);
        Assert.Equal(1, duplicateGeneration.StableObservations);
        Assert.Null(duplicateGeneration.Ticket);
        Assert.Equal(2, second.StableObservations);
        Assert.True(second.Ticket.HasValue);
        Assert.True(second.Ticket.Value.IsValid);
        Assert.Equal(1, second.Ticket.Value.Fingerprint.ExcessStacks);
    }

    [Fact]
    public void ChangedFingerprintAndLifecycleEachRequireFreshStableEvidence()
    {
        var tracker = new ActionQueueIntegrityTracker();
        tracker.Observe(Observation(50, stacks: 2, pending: 0));

        var changed = tracker.Observe(Observation(51, stacks: 1, pending: 0));
        var changedStable = tracker.Observe(Observation(52, stacks: 1, pending: 0));
        var newLifecycle = tracker.Observe(Observation(
            53,
            stacks: 1,
            pending: 0,
            lifecycle: 8));

        Assert.Equal(1, changed.StableObservations);
        Assert.Null(changed.Ticket);
        Assert.True(changedStable.Ticket.HasValue);
        Assert.Equal(1, newLifecycle.StableObservations);
        Assert.Null(newLifecycle.Ticket);
    }

    [Fact]
    public void OneFingerprintReceivesOnlyOneTicketPerLifecycle()
    {
        var tracker = new ActionQueueIntegrityTracker();
        tracker.Observe(Observation(60, stacks: 1, pending: 0));
        Assert.True(tracker.Observe(Observation(61, stacks: 1, pending: 0)).Ticket.HasValue);

        Assert.Null(tracker.Observe(Observation(62, stacks: 1, pending: 0)).Ticket);
        Assert.Null(tracker.Observe(Observation(63, stacks: 1, pending: 0)).Ticket);
    }

    private static ActionQueueMemberObservation Observation(
        ulong generation,
        int stacks,
        int pending,
        string exactType = ActionQueueIntegrityClassifier.StructureNativeType,
        bool afterRestart = false,
        long lifecycle = 7) =>
        new(
            lifecycle,
            generation,
            QueueId,
            MemberId,
            exactType,
            stacks,
            pending,
            totalStacks: Math.Max(stacks, 0) + 12,
            remainingRoom: 20,
            observedAfterRestart: afterRestart);
}
