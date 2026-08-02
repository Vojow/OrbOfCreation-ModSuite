using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-bound mutation boundary for the global Casting-screen Output Level.</summary>
internal sealed class SpellCompositionGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private SpellCompositionNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal SpellCompositionGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal SpellCompositionSubmission Submit(in SpellCompositionAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.WrongThread,
                "The Casting Output Level dial is bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Casting Output Level binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (currentEpoch != action.LifecycleEpoch)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; the live lifecycle is " + currentEpoch + ".");

        try { return SetOutputLevel(in action, native); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Casting Output Level preflight failed before mutation: " +
                ex.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private SpellCompositionSubmission SetOutputLevel(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        var player = native.ReadPlayer();
        var output = native.ReadOutputVariable();
        if (player is null || output is null)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Player output-level state is not initialized in this lifecycle.");
        var current = native.ReadInt(output);
        var maximum = native.ReadInt(native.ReadMaximumOutputVariable(player));
        if (action.OutputLevel < 1 || action.OutputLevel > maximum)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.OutputLevelOutOfRange,
                "Requested Output Level " + action.OutputLevel +
                " is outside the live native range 1.." + maximum + ".");
        if (current == action.OutputLevel)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.AlreadyInRequestedState,
                "The global Output Level is already " + current + ".");
        var before = Capture(native);
        if (!TryCapturePermit(out var reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.SetInt(output, action.OutputLevel);
            var after = Capture(native);
            return after.OutputLevel == action.OutputLevel
                ? Verified(in before, in after,
                    "The global Output Level is now " + action.OutputLevel + ".")
                : Fault(
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    in before,
                    in after,
                    "The global Output Level variable did not hold the requested value.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, in before);
            if (after.OutputLevel == action.OutputLevel)
                return Verified(in before, in after,
                    "The Output Level setter threw after the requested value became observable.");
            return Fault(
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.OutputLevel,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                "The Output Level setter threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static SpellCompositionState Capture(SpellCompositionNativeBindings native)
    {
        var player = native.ReadPlayer() ??
            throw new InvalidOperationException("Player._instance was null.");
        var output = native.ReadOutputVariable() ??
            throw new InvalidOperationException("Player.GetSpellOutputLevel() returned null.");
        return new SpellCompositionState(
            native.ReadInt(output),
            native.ReadInt(native.ReadMaximumOutputVariable(player)));
    }

    private static SpellCompositionState CaptureBestEffort(
        SpellCompositionNativeBindings native,
        in SpellCompositionState fallback)
    {
        try { return Capture(native); }
        catch (Exception ex) when (IsExpected(ex)) { return fallback; }
    }

    private static SpellCompositionSubmission Verified(
        in SpellCompositionState before,
        in SpellCompositionState after,
        string reason)
    {
        var evidence = new SpellCompositionEvidence(true, in before, in after);
        return new SpellCompositionSubmission(
            SpellCompositionPreflight.Proceeded,
            SpellCompositionNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            in evidence,
            reason);
    }

    private static SpellCompositionSubmission Fault(
        SpellCompositionPreflight preflight,
        SpellCompositionNativeStage stage,
        NativeMutationOutcome outcome,
        in SpellCompositionState before,
        in SpellCompositionState after,
        string reason)
    {
        var evidence = new SpellCompositionEvidence(true, in before, in after);
        return new SpellCompositionSubmission(
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(1, 1, 0),
            in evidence,
            "Casting Output Level faulted after " + stage + ": " + reason);
    }

    private bool TryCapturePermit(out string reason)
    {
        if (_tryCaptureMutationPermit())
        {
            reason = string.Empty;
            return true;
        }
        reason = _readOwnershipFailure();
        if (reason.Length == 0)
            reason = "The suite does not own the Casting Output Level action family.";
        return false;
    }

    private void BindLifecycle()
    {
        var resolve = _resolveType ?? ReflectionUtil.FindLoadedType;
        var include = _includeContract ?? (_ => true);
        if (!SpellCompositionNativeBindings.TryCreate(
                resolve,
                include,
                out _bindings,
                out _bindingFailure))
            _bindings = null;
    }

    private static bool IsExpected(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or OverflowException or
            TargetInvocationException or MemberAccessException;
}
