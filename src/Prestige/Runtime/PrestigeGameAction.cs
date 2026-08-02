using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for persistent reset.</summary>
internal sealed class PrestigeGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private PrestigeNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal PrestigeGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal bool IsQuarantined => _quarantineReason.Length != 0;

    internal PrestigeSubmission Submit(in PrestigeAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return PrestigeSubmission.Reject(PrestigePreflight.WrongThread,
                "Prestige is bound to Unity thread " + _mainThreadId + ".");
        if (_quarantineReason.Length != 0)
            return PrestigeSubmission.Reject(PrestigePreflight.Quarantined, _quarantineReason);
        if (_bindings is not { } native)
            return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return PrestigeSubmission.Reject(PrestigePreflight.LifecycleReplaced,
                "The lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return PrestigeSubmission.Reject(PrestigePreflight.LifecycleReplaced,
                "The submitted lifecycle is stale.");

        try
        {
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable,
                    "The native persistent reset manager was unavailable.");
            if (!TryCapture(native, manager, epoch, out var before, out var captureFailure))
                return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable, captureFailure);
            if (!before.WorldCycleComplete)
                return PrestigeSubmission.Reject(PrestigePreflight.WorldCycleIncomplete,
                    "Persistent reset is unavailable until the current world cycle is complete.");
            if (!before.ChallengesFetched)
                return PrestigeSubmission.Reject(PrestigePreflight.ChallengesNotFetched,
                    "Persistent reset is unavailable until prestige challenges have been fetched and chosen.");
            if (!_tryCaptureMutationPermit())
                return PrestigeSubmission.Reject(PrestigePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(native, manager, in before);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return PrestigeSubmission.Reject(PrestigePreflight.ContractUnavailable,
                "Prestige preflight failed before mutation: " + exception.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
    }

    private PrestigeSubmission Execute(PrestigeNativeBindings native, object manager,
        in PrestigeState before)
    {
        var stage = PrestigeNativeStage.NativeTransaction;
        try
        {
            // PersistentReset() only schedules this transaction behind UIScreenFlash.FadeIn.
            // Tooling has no rendered-modal dependency, so invoke the exact transaction directly.
            native.Reset(manager);
            stage = PrestigeNativeStage.Verification;
            var observedEpoch = _readLifecycleEpoch();
            var after = new PrestigeState(false, observedEpoch, false, false, 0);
            var receipt = new PrestigeReceipt(in before, in after);
            if (observedEpoch == checked(before.LifecycleEpoch + 1))
                return new PrestigeSubmission(PrestigePreflight.Proceeded, stage,
                    NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
                    in receipt, "Verified the exact persistent-reset lifecycle transition.");
            return Quarantine(PrestigePreflight.VerificationFailed, stage,
                NativeMutationOutcome.PostconditionFailed, in receipt,
                "The native reset transaction did not replace the lifecycle exactly once.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            long observedEpoch;
            try { observedEpoch = _readLifecycleEpoch(); }
            catch (Exception) { observedEpoch = before.LifecycleEpoch; }
            var after = new PrestigeState(false, observedEpoch, false, false, 0);
            var receipt = new PrestigeReceipt(in before, in after);
            return Quarantine(PrestigePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, in receipt,
                "The native reset transaction threw after invocation; full reset completion is unproven: " +
                exception.GetBaseException().Message);
        }
    }

    private static bool TryCapture(PrestigeNativeBindings native, object manager, long epoch,
        out PrestigeState state, out string reason)
    {
        var complete = native.CycleComplete(manager);
        var fetched = native.ChallengesFetched(manager);
        var count = native.ResetCount(manager);
        if (complete is null || fetched is null || count is null)
        {
            state = default;
            reason = "The native prestige decision graph returned a null member.";
            return false;
        }
        state = new PrestigeState(true, epoch, native.GetBool(complete),
            native.GetBool(fetched), native.AsInt(count));
        reason = string.Empty;
        return true;
    }

    private PrestigeSubmission Quarantine(PrestigePreflight preflight,
        PrestigeNativeStage stage, NativeMutationOutcome outcome,
        in PrestigeReceipt receipt, string reason)
    {
        _quarantineReason = "Prestige is quarantined for this lifecycle after " + stage + ": " + reason;
        return new PrestigeSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), in receipt, _quarantineReason);
    }

    private void BindLifecycle()
    {
        if (PrestigeNativeBindings.TryCreate(out var bindings, out var reason,
                _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is InvalidOperationException or
        ArgumentException or TargetInvocationException or OverflowException;
}
