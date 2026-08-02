using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The sole mutation boundary for the current native targeting request.</summary>
internal sealed class TargetingGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private TargetingNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal TargetingGameAction(Func<long> readLifecycleEpoch, Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure, Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType; _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal bool IsQuarantined => _quarantineReason.Length != 0;
    internal string BindingFailure => _bindingFailure;

    internal TargetingSubmission Submit(in TargetingAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return TargetingSubmission.Reject(TargetingPreflight.WrongThread,
                "Targeting actions are bound to Unity thread " + _mainThreadId + ".");
        if (_quarantineReason.Length != 0)
            return TargetingSubmission.Reject(TargetingPreflight.Quarantined, _quarantineReason);
        if (_bindings is not { } native)
            return TargetingSubmission.Reject(TargetingPreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (Expected(ex))
        {
            return TargetingSubmission.Reject(TargetingPreflight.LifecycleReplaced,
                "The live lifecycle could not be read: " + ex.GetBaseException().Message);
        }
        if (epoch != action.LifecycleEpoch)
            return TargetingSubmission.Reject(TargetingPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch + " is stale; live lifecycle is " + epoch + ".");
        try
        {
            if (!native.IsTargeting())
                return TargetingSubmission.Reject(TargetingPreflight.NoPendingRequest,
                    "The game has no pending target request.");
            var link = native.GetLink();
            if (link is null || link.GetType() != native.LinkType)
                return TargetingSubmission.Reject(TargetingPreflight.NoPendingRequest,
                    "Native targeting did not expose one exact current TargetLink.");
            return action.Kind switch
            {
                TargetingActionKind.Submit => SubmitTarget(in action, native, link),
                TargetingActionKind.Randomize => Randomize(in action, native, link),
                TargetingActionKind.Cancel => Cancel(in action, native, link),
                _ => TargetingSubmission.Reject(TargetingPreflight.ContractUnavailable, "Unknown targeting mode."),
            };
        }
        catch (Exception ex) when (Expected(ex))
        {
            return TargetingSubmission.Reject(TargetingPreflight.ContractUnavailable,
                "Targeting preflight failed before mutation: " + ex.GetBaseException().Message);
        }
    }

    private TargetingSubmission SubmitTarget(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        var candidates = native.GetAllTargets(link);
        object? candidate = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var item = candidates[index];
            if (item is not null && item.GetType() == native.StructureType &&
                native.GetGuid(item) == action.TargetId)
            {
                if (candidate is not null)
                    return TargetingSubmission.Reject(TargetingPreflight.TargetUnavailable,
                        "Target UUID is ambiguous in the live eligible-target set.");
                candidate = item;
            }
        }
        if (candidate is null)
            return TargetingSubmission.Reject(TargetingPreflight.TargetUnavailable,
                "Target UUID is absent from the live eligible-target set.");
        if (!native.CheckTarget(link, candidate))
            return TargetingSubmission.Reject(TargetingPreflight.NativeTargetRefused,
                "TargetLink.CheckTarget refused the exact UUID-resolved StructureSO.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingPreflight.MutationPermitUnavailable, reason);
        return MutateSubmit(in action, native, link, candidate, TargetingNativeStage.Submit);
    }

    private TargetingSubmission Randomize(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        if (native.GetAllTargets(link).Count == 0)
            return TargetingSubmission.Reject(TargetingPreflight.TargetUnavailable,
                "The current request has no eligible StructureSO targets.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingPreflight.MutationPermitUnavailable, reason);
        object? candidate;
        try { candidate = native.GetRandom(link); }
        catch (Exception ex) when (Expected(ex))
        {
            return Quarantine(TargetingPreflight.PostCommitFault, TargetingNativeStage.SelectRandom,
                NativeMutationOutcome.ExecutionThrew, in action, Guid.Empty, native.IsTargeting(),
                new NativeMutationCallOutcome(1, 1, 0),
                "TargetLink.GetRandom threw after random selection began: " + ex.GetBaseException().Message);
        }
        if (candidate is null || candidate.GetType() != native.StructureType)
            return Quarantine(TargetingPreflight.VerificationFailed, TargetingNativeStage.SelectRandom,
                NativeMutationOutcome.PostconditionFailed, in action, Guid.Empty, native.IsTargeting(),
                new NativeMutationCallOutcome(1, 1, 0),
                "TargetLink.GetRandom did not return one exact StructureSO.");
        if (!native.CheckTarget(link, candidate))
            return Quarantine(TargetingPreflight.VerificationFailed, TargetingNativeStage.SelectRandom,
                NativeMutationOutcome.PostconditionFailed, in action, native.GetGuid(candidate), native.IsTargeting(),
                new NativeMutationCallOutcome(1, 1, 0),
                "The native random target failed the same TargetLink.CheckTarget verdict.");
        return MutateSubmit(in action, native, link, candidate, TargetingNativeStage.Submit, 1);
    }

    private TargetingSubmission MutateSubmit(in TargetingAction action,
        TargetingNativeBindings native, object link, object candidate,
        TargetingNativeStage stage, int priorCalls = 0)
    {
        var id = native.GetGuid(candidate);
        try
        {
            native.SubmitTarget(candidate);
            return Submitted(native, link, candidate)
                ? Verified(in action, id, native.IsTargeting(), priorCalls + 1,
                    "The exact target was assigned and its original request left the queue.")
                : Quarantine(TargetingPreflight.VerificationFailed, TargetingNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, in action, id, native.IsTargeting(),
                    new NativeMutationCallOutcome(priorCalls + 1, 1, 0),
                    "SubmitTarget did not assign the exact object and retire its request.");
        }
        catch (Exception ex) when (Expected(ex))
        {
            if (Submitted(native, link, candidate))
                return Verified(in action, id, native.IsTargeting(), priorCalls + 1,
                    "SubmitTarget threw after the exact requested outcome became observable.");
            return Quarantine(TargetingPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, in action, id, SafeIsTargeting(native),
                new NativeMutationCallOutcome(priorCalls + 1, 1, 0),
                "SubmitTarget threw before the requested outcome was observable: " + ex.GetBaseException().Message);
        }
    }

    private TargetingSubmission Cancel(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        var resultInfo = native.ReadResultInfo(link);
        if (resultInfo is null)
            return TargetingSubmission.Reject(TargetingPreflight.CancelUnavailable,
                "The current TargetLink has no EffectResultInfo cancellation owner.");
        if (!TryPermit(out var reason))
            return TargetingSubmission.Reject(TargetingPreflight.MutationPermitUnavailable, reason);
        try
        {
            native.Cancel(resultInfo);
            return native.IsCancelled(resultInfo) && OriginalRequestGone(native, link)
                ? Verified(in action, Guid.Empty, native.IsTargeting(), 1,
                    "EffectResultInfo is cancelled and its exact request left the queue.")
                : Quarantine(TargetingPreflight.VerificationFailed, TargetingNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, in action, Guid.Empty, native.IsTargeting(),
                    new NativeMutationCallOutcome(1, 1, 0),
                    "Cancel did not cancel its EffectResultInfo and retire the exact request.");
        }
        catch (Exception ex) when (Expected(ex))
        {
            if (native.IsCancelled(resultInfo) && OriginalRequestGone(native, link))
                return Verified(in action, Guid.Empty, native.IsTargeting(), 1,
                    "Cancel threw after the exact requested outcome became observable.");
            return Quarantine(TargetingPreflight.PostCommitFault, TargetingNativeStage.Cancel,
                NativeMutationOutcome.ExecutionThrew, in action, Guid.Empty, SafeIsTargeting(native),
                new NativeMutationCallOutcome(1, 1, 0),
                "Cancel threw before the requested outcome was observable: " + ex.GetBaseException().Message);
        }
    }

    private static bool Submitted(TargetingNativeBindings native, object link, object candidate) =>
        native.HasTarget(link) && ReferenceEquals(native.ReadTarget(link), candidate) &&
        OriginalRequestGone(native, link);
    private static bool OriginalRequestGone(TargetingNativeBindings native, object link) =>
        !native.IsTargeting() || !ReferenceEquals(native.GetLink(), link);
    private static bool SafeIsTargeting(TargetingNativeBindings native)
    { try { return native.IsTargeting(); } catch (Exception) { return false; } }

    private TargetingSubmission Verified(in TargetingAction action, Guid submitted,
        bool pendingAfter, int calls, string reason)
    {
        var evidence = new TargetingEvidence(true, action.TargetId, submitted, true, pendingAfter);
        return new TargetingSubmission(TargetingPreflight.Proceeded, TargetingNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(calls, 1, 1), in evidence, reason);
    }
    private TargetingSubmission Quarantine(TargetingPreflight preflight, TargetingNativeStage stage,
        NativeMutationOutcome outcome, in TargetingAction action, Guid submitted, bool pendingAfter,
        NativeMutationCallOutcome calls, string reason)
    {
        _quarantineReason = "Targeting is quarantined for this lifecycle after " + action.Kind +
            " could not prove its exact outcome: " + reason;
        var evidence = new TargetingEvidence(true, action.TargetId, submitted, true, pendingAfter);
        return new TargetingSubmission(preflight, stage, outcome, calls, in evidence, _quarantineReason);
    }
    private bool TryPermit(out string reason)
    {
        if (_tryCaptureMutationPermit()) { reason = string.Empty; return true; }
        reason = _readOwnershipFailure();
        if (string.IsNullOrWhiteSpace(reason)) reason = "The targeting action family is not owned.";
        return false;
    }
    internal void InvalidateLifecycle() { _bindings = null; _bindingFailure = string.Empty; _quarantineReason = string.Empty; BindLifecycle(); }
    public void Dispose() { _bindings = null; _bindingFailure = string.Empty; _quarantineReason = string.Empty; }
    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!TargetingNativeBindings.TryCreate(resolve, include, out _bindings, out _bindingFailure)) _bindings = null;
    }
    private static bool Expected(Exception ex) => ex is not StackOverflowException and
        not OutOfMemoryException and not AccessViolationException;
}
