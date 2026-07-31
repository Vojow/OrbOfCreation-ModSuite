using System;
using System.Collections.Generic;

namespace OrbAutomata;

/// <summary>Fresh native evidence captured at the recovery action boundary.</summary>
internal readonly struct ActionQueueRecoveryNativeState
{
    internal ActionQueueRecoveryNativeState(
        long lifecycle,
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        int memberStacks,
        int authoritativePending,
        int totalStacks,
        int remainingRoom)
    {
        Lifecycle = lifecycle;
        QueueId = queueId;
        MemberId = memberId;
        ExactNativeType = exactNativeType ?? string.Empty;
        MemberStacks = memberStacks;
        AuthoritativePending = authoritativePending;
        TotalStacks = totalStacks;
        RemainingRoom = remainingRoom;
    }

    internal long Lifecycle { get; }
    internal Guid QueueId { get; }
    internal Guid MemberId { get; }
    internal string ExactNativeType { get; }
    internal int MemberStacks { get; }
    internal int AuthoritativePending { get; }
    internal int TotalStacks { get; }
    internal int RemainingRoom { get; }
}

/// <summary>
/// Native recovery seam. Implementations resolve exact UUID plus exact type and access Unity/game
/// state only from the Unity main thread.
/// </summary>
internal interface IActionQueueRecoveryNativePort
{
    bool IsMainThread { get; }

    bool TryCapture(
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        out ActionQueueRecoveryNativeState state,
        out string reason);

    bool TryUnloadExactExcess(
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        int excessStacks,
        out string reason);
}

internal enum ActionQueueRecoveryOutcome
{
    RejectedInvalidTicket = 0,
    RejectedWrongThread = 1,
    RejectedAlreadyAttempted = 2,
    RejectedStaleEvidence = 3,
    CaptureFailed = 4,
    UnloadFailed = 5,
    VerificationFailed = 6,
    Committed = 7,
}

internal readonly struct ActionQueueRecoveryResult
{
    internal ActionQueueRecoveryResult(
        ActionQueueRecoveryOutcome outcome,
        bool mutationAttempted,
        int unloadedStacks,
        string reason)
    {
        Outcome = outcome;
        MutationAttempted = mutationAttempted;
        UnloadedStacks = unloadedStacks;
        Reason = reason ?? string.Empty;
    }

    internal ActionQueueRecoveryOutcome Outcome { get; }
    internal bool MutationAttempted { get; }
    internal int UnloadedStacks { get; }
    internal string Reason { get; }
    internal bool IsCommitted => Outcome == ActionQueueRecoveryOutcome.Committed;
}

/// <summary>
/// One ticketed action-queue recovery transaction. It revalidates the exact member immediately,
/// unloads only the proven excess, and verifies that no authoritative pending work changed.
/// </summary>
internal sealed class ActionQueueRecoveryGameAction
{
    private readonly IActionQueueRecoveryNativePort _native;
    private readonly HashSet<ActionQueueRecoveryFingerprint> _attempted = new();

    internal ActionQueueRecoveryGameAction(IActionQueueRecoveryNativePort native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    internal ActionQueueRecoveryResult Execute(in ActionQueueRecoveryTicket ticket)
    {
        if (!ticket.IsValid ||
            ticket.Fingerprint.ExcessStacks <= 0 ||
            (ticket.Fingerprint.ObservedAfterRestart &&
             string.Equals(
                 ticket.Fingerprint.ExactNativeType,
                 ActionQueueIntegrityClassifier.UpgradeNativeType,
                 StringComparison.Ordinal)))
        {
            return Reject(
                ActionQueueRecoveryOutcome.RejectedInvalidTicket,
                "The recovery request did not carry an eligible exact queue-integrity ticket.");
        }
        if (!_native.IsMainThread)
        {
            return Reject(
                ActionQueueRecoveryOutcome.RejectedWrongThread,
                "Action-queue recovery must run on the Unity main thread.");
        }
        if (!_attempted.Add(ticket.Fingerprint))
        {
            return Reject(
                ActionQueueRecoveryOutcome.RejectedAlreadyAttempted,
                "This exact action-queue recovery fingerprint already consumed its one attempt.");
        }

        if (!TryCapture(ticket, out var before, out var captureFailure))
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.CaptureFailed,
                false,
                0,
                captureFailure);
        }
        if (!MatchesTicket(in before, in ticket, out var mismatch))
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.RejectedStaleEvidence,
                false,
                0,
                mismatch);
        }

        var excess = ticket.Fingerprint.ExcessStacks;
        bool unloaded;
        string unloadFailure;
        try
        {
            unloaded = _native.TryUnloadExactExcess(
                before.QueueId,
                before.MemberId,
                before.ExactNativeType,
                excess,
                out unloadFailure);
        }
        catch (Exception ex)
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.UnloadFailed,
                true,
                0,
                "The native exact-excess unload threw: " + ex.GetBaseException().Message);
        }
        if (!unloaded)
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.UnloadFailed,
                true,
                0,
                string.IsNullOrWhiteSpace(unloadFailure)
                    ? "The native exact-excess unload was refused without a reason."
                    : unloadFailure);
        }

        if (!TryCapture(ticket, out var after, out var verificationCaptureFailure))
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.VerificationFailed,
                true,
                0,
                "The native unload was attempted, but post-state capture failed: " +
                verificationCaptureFailure);
        }
        if (!VerifyPostcondition(in before, in after, excess, out var verificationFailure))
        {
            return new ActionQueueRecoveryResult(
                ActionQueueRecoveryOutcome.VerificationFailed,
                true,
                0,
                verificationFailure);
        }

        return new ActionQueueRecoveryResult(
            ActionQueueRecoveryOutcome.Committed,
            true,
            excess,
            $"Verified exact recovery of {excess} excess stack(s) from " +
            $"{before.ExactNativeType} {before.MemberId:D}; authoritative pending work remained " +
            $"{before.AuthoritativePending}.");
    }

    internal void InvalidateLifecycle() => _attempted.Clear();

    private bool TryCapture(
        in ActionQueueRecoveryTicket ticket,
        out ActionQueueRecoveryNativeState state,
        out string reason)
    {
        try
        {
            if (_native.TryCapture(
                    ticket.Fingerprint.QueueId,
                    ticket.Fingerprint.MemberId,
                    ticket.Fingerprint.ExactNativeType,
                    out state,
                    out reason))
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(reason))
                reason = "The native action-queue state capture was refused without a reason.";
            return false;
        }
        catch (Exception ex)
        {
            state = default;
            reason = "The native action-queue state capture threw: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool MatchesTicket(
        in ActionQueueRecoveryNativeState state,
        in ActionQueueRecoveryTicket ticket,
        out string reason)
    {
        var fingerprint = ticket.Fingerprint;
        if (state.Lifecycle != fingerprint.Lifecycle ||
            state.QueueId != fingerprint.QueueId ||
            state.MemberId != fingerprint.MemberId ||
            !string.Equals(
                state.ExactNativeType,
                fingerprint.ExactNativeType,
                StringComparison.Ordinal) ||
            state.MemberStacks != fingerprint.MemberStacks ||
            state.AuthoritativePending != fingerprint.AuthoritativePending ||
            state.MemberStacks - state.AuthoritativePending != fingerprint.ExcessStacks ||
            state.MemberStacks < 0 ||
            state.AuthoritativePending < 0 ||
            state.TotalStacks < state.MemberStacks ||
            state.RemainingRoom < 0)
        {
            reason =
                "Fresh native revalidation no longer matches the exact ticket: " +
                $"lifecycle={state.Lifecycle}, queue={state.QueueId:D}, member={state.MemberId:D}, " +
                $"type={state.ExactNativeType}, stacks={state.MemberStacks}, " +
                $"pending={state.AuthoritativePending}, total={state.TotalStacks}, " +
                $"room={state.RemainingRoom}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool VerifyPostcondition(
        in ActionQueueRecoveryNativeState before,
        in ActionQueueRecoveryNativeState after,
        int excess,
        out string reason)
    {
        if (after.Lifecycle == before.Lifecycle &&
            after.QueueId == before.QueueId &&
            after.MemberId == before.MemberId &&
            string.Equals(after.ExactNativeType, before.ExactNativeType, StringComparison.Ordinal) &&
            after.MemberStacks == (long)before.MemberStacks - excess &&
            after.AuthoritativePending == before.AuthoritativePending &&
            after.TotalStacks == (long)before.TotalStacks - excess &&
            after.RemainingRoom == (long)before.RemainingRoom + excess)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            "Exact-excess unload postcondition was not proven: " +
            $"before(stacks={before.MemberStacks},pending={before.AuthoritativePending}," +
            $"total={before.TotalStacks},room={before.RemainingRoom}); " +
            $"after(stacks={after.MemberStacks},pending={after.AuthoritativePending}," +
            $"total={after.TotalStacks},room={after.RemainingRoom}); expectedExcess={excess}.";
        return false;
    }

    private static ActionQueueRecoveryResult Reject(
        ActionQueueRecoveryOutcome outcome,
        string reason) =>
        new(outcome, false, 0, reason);
}

/// <summary>
/// Feature-neutral composition seam. Observation may issue a ticket; executing that ticket is a
/// separate explicit main-thread action.
/// </summary>
internal sealed class ActionQueueRecoveryAdapter
{
    private readonly ActionQueueIntegrityTracker _tracker;
    private readonly ActionQueueRecoveryGameAction _gameAction;

    internal ActionQueueRecoveryAdapter(IActionQueueRecoveryNativePort native)
    {
        _tracker = new ActionQueueIntegrityTracker();
        _gameAction = new ActionQueueRecoveryGameAction(native);
    }

    internal ActionQueueIntegrityTrackingResult Observe(
        in ActionQueueMemberObservation observation) =>
        _tracker.Observe(in observation);

    internal ActionQueueRecoveryResult Execute(in ActionQueueRecoveryTicket ticket) =>
        _gameAction.Execute(in ticket);

    internal void InvalidateLifecycle()
    {
        _tracker.InvalidateLifecycle();
        _gameAction.InvalidateLifecycle();
    }
}
