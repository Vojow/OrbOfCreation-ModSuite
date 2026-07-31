using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.Coordination;

public sealed class ActionQueueRecoveryGameActionTests
{
    private static readonly Guid QueueId = new("2c4825f4-9869-41fc-84df-301638a097a5");
    private static readonly Guid MemberId = new("7c04fa78-66de-4941-8f7f-e7964f841031");

    [Fact]
    public void ExactTicketRevalidatesUnloadsOnlyExcessAndProvesEveryDelta()
    {
        var native = new FakeNativePort(State(stacks: 5, pending: 4, total: 20, room: 112));
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(stacks: 5, pending: 4);

        var result = action.Execute(in ticket);

        Assert.True(result.IsCommitted);
        Assert.True(result.MutationAttempted);
        Assert.Equal(1, result.UnloadedStacks);
        Assert.Equal(1, native.UnloadCalls);
        Assert.Equal(1, native.LastUnloadCount);
        Assert.Equal(4, native.State.MemberStacks);
        Assert.Equal(4, native.State.AuthoritativePending);
        Assert.Equal(19, native.State.TotalStacks);
        Assert.Equal(113, native.State.RemainingRoom);
        Assert.Contains("authoritative pending work remained 4", result.Reason);
    }

    [Fact]
    public void FreshMemberMismatchRejectsWithoutUnloadAndConsumesTheFingerprintAttempt()
    {
        var native = new FakeNativePort(State(stacks: 4, pending: 4, total: 19, room: 113));
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(stacks: 5, pending: 4);

        var stale = action.Execute(in ticket);
        var replay = action.Execute(in ticket);

        Assert.Equal(ActionQueueRecoveryOutcome.RejectedStaleEvidence, stale.Outcome);
        Assert.False(stale.MutationAttempted);
        Assert.Equal(0, native.UnloadCalls);
        Assert.Equal(ActionQueueRecoveryOutcome.RejectedAlreadyAttempted, replay.Outcome);
    }

    [Fact]
    public void WrongThreadRejectsBeforeNativeReadsWithoutConsumingTheTicket()
    {
        var native = new FakeNativePort(State(1, 0, 10, 20)) { IsMainThread = false };
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(1, 0);

        var wrongThread = action.Execute(in ticket);
        native.IsMainThread = true;
        var committed = action.Execute(in ticket);

        Assert.Equal(ActionQueueRecoveryOutcome.RejectedWrongThread, wrongThread.Outcome);
        Assert.Equal(2, native.CaptureCalls);
        Assert.True(committed.IsCommitted);
    }

    [Fact]
    public void AuthoritativePendingMutationFailsPostconditionAndCannotRetry()
    {
        var native = new FakeNativePort(State(2, 1, 10, 20))
        {
            MutateAuthoritativePendingDuringUnload = true,
        };
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(2, 1);

        var failed = action.Execute(in ticket);
        var replay = action.Execute(in ticket);

        Assert.Equal(ActionQueueRecoveryOutcome.VerificationFailed, failed.Outcome);
        Assert.True(failed.MutationAttempted);
        Assert.Equal(0, failed.UnloadedStacks);
        Assert.Contains("pending=1", failed.Reason);
        Assert.Contains("pending=0", failed.Reason);
        Assert.Equal(ActionQueueRecoveryOutcome.RejectedAlreadyAttempted, replay.Outcome);
        Assert.Equal(1, native.UnloadCalls);
    }

    [Fact]
    public void WrongTotalOrRoomDeltaCannotBeReportedAsRecovery()
    {
        var native = new FakeNativePort(State(3, 1, 10, 20))
        {
            SuppressTotalStackDelta = true,
            SuppressRoomDelta = true,
        };
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(3, 1);

        var result = action.Execute(in ticket);

        Assert.Equal(ActionQueueRecoveryOutcome.VerificationFailed, result.Outcome);
        Assert.True(result.MutationAttempted);
        Assert.Equal(0, result.UnloadedStacks);
        Assert.Contains("expectedExcess=2", result.Reason);
    }

    [Fact]
    public void NativeUnloadThrowIsAttemptedAndBoundedToOneCall()
    {
        var native = new FakeNativePort(State(1, 0, 10, 20)) { ThrowOnUnload = true };
        var action = new ActionQueueRecoveryGameAction(native);
        var ticket = Ticket(1, 0);

        var result = action.Execute(in ticket);
        var replay = action.Execute(in ticket);

        Assert.Equal(ActionQueueRecoveryOutcome.UnloadFailed, result.Outcome);
        Assert.True(result.MutationAttempted);
        Assert.Contains("injected unload failure", result.Reason);
        Assert.Equal(ActionQueueRecoveryOutcome.RejectedAlreadyAttempted, replay.Outcome);
        Assert.Equal(1, native.UnloadCalls);
    }

    [Fact]
    public void AdapterSeparatesObservationTicketIssuanceFromExplicitRecovery()
    {
        var native = new FakeNativePort(State(1, 0, 10, 20));
        var adapter = new ActionQueueRecoveryAdapter(native);
        var first = adapter.Observe(Observation(generation: 1, stacks: 1, pending: 0));
        var second = adapter.Observe(Observation(generation: 2, stacks: 1, pending: 0));

        Assert.Null(first.Ticket);
        Assert.Equal(0, native.CaptureCalls);
        Assert.Equal(0, native.UnloadCalls);
        var ticket = Assert.IsType<ActionQueueRecoveryTicket>(second.Ticket);

        var result = adapter.Execute(in ticket);

        Assert.True(result.IsCommitted);
        Assert.Equal(1, native.UnloadCalls);
    }

    private static ActionQueueRecoveryTicket Ticket(int stacks, int pending)
    {
        var tracker = new ActionQueueIntegrityTracker();
        tracker.Observe(Observation(100, stacks, pending));
        return tracker.Observe(Observation(101, stacks, pending)).Ticket!.Value;
    }

    private static ActionQueueMemberObservation Observation(
        ulong generation,
        int stacks,
        int pending) =>
        new(
            lifecycle: 7,
            publicationGeneration: generation,
            QueueId,
            MemberId,
            ActionQueueIntegrityClassifier.StructureNativeType,
            stacks,
            pending,
            totalStacks: 20,
            remainingRoom: 112,
            observedAfterRestart: false);

    private static ActionQueueRecoveryNativeState State(
        int stacks,
        int pending,
        int total,
        int room) =>
        new(
            lifecycle: 7,
            QueueId,
            MemberId,
            ActionQueueIntegrityClassifier.StructureNativeType,
            stacks,
            pending,
            total,
            room);

    private sealed class FakeNativePort : IActionQueueRecoveryNativePort
    {
        internal FakeNativePort(ActionQueueRecoveryNativeState state) => State = state;

        public bool IsMainThread { get; set; } = true;
        internal ActionQueueRecoveryNativeState State { get; private set; }
        internal int CaptureCalls { get; private set; }
        internal int UnloadCalls { get; private set; }
        internal int LastUnloadCount { get; private set; }
        internal bool ThrowOnUnload { get; set; }
        internal bool MutateAuthoritativePendingDuringUnload { get; set; }
        internal bool SuppressTotalStackDelta { get; set; }
        internal bool SuppressRoomDelta { get; set; }

        public bool TryCapture(
            Guid queueId,
            Guid memberId,
            string exactNativeType,
            out ActionQueueRecoveryNativeState state,
            out string reason)
        {
            CaptureCalls++;
            state = State;
            reason = string.Empty;
            return true;
        }

        public bool TryUnloadExactExcess(
            Guid queueId,
            Guid memberId,
            string exactNativeType,
            int excessStacks,
            out string reason)
        {
            UnloadCalls++;
            LastUnloadCount = excessStacks;
            if (ThrowOnUnload) throw new InvalidOperationException("injected unload failure");

            State = new ActionQueueRecoveryNativeState(
                State.Lifecycle,
                State.QueueId,
                State.MemberId,
                State.ExactNativeType,
                State.MemberStacks - excessStacks,
                MutateAuthoritativePendingDuringUnload
                    ? State.AuthoritativePending - 1
                    : State.AuthoritativePending,
                SuppressTotalStackDelta
                    ? State.TotalStacks
                    : State.TotalStacks - excessStacks,
                SuppressRoomDelta
                    ? State.RemainingRoom
                    : State.RemainingRoom + excessStacks);
            reason = string.Empty;
            return true;
        }
    }
}
