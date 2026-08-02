using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-bound mutation boundary for both global Casting-screen dials.</summary>
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
                "The Casting dial is bound to Unity thread " + _mainThreadId +
                ", not thread " + Environment.CurrentManagedThreadId + ".");
        if (_bindings is not { } native)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Casting-dial binding set is unavailable."
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

        try { return SetDial(in action, native); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Casting-dial preflight failed before mutation: " +
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

    private SpellCompositionSubmission SetDial(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        var player = native.ReadPlayer();
        var variable = ReadVariable(native, action.Dial);
        if (player is null || variable is null)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.ContractUnavailable,
                "Player " + Name(action.Dial) + " state is not initialized in this lifecycle.");
        var current = native.ReadInt(variable);
        var maximum = native.ReadInt(ReadMaximumVariable(native, player, action.Dial));
        if (action.Value < 1 || action.Value > maximum)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.LevelOutOfRange,
                "Requested " + Name(action.Dial) + " " + action.Value +
                " is outside the live native range 1.." + maximum + ".");
        if (current == action.Value)
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.AlreadyInRequestedState,
                "The global " + Name(action.Dial) + " is already " + current + ".");
        var before = Capture(native, player, action.Dial);
        if (!TryCapturePermit(out var reason))
            return SpellCompositionSubmission.Reject(
                SpellCompositionPreflight.MutationPermitUnavailable,
                reason);

        try
        {
            native.SetInt(variable, action.Value);
            var after = Capture(native, player, action.Dial);
            return after.Current == action.Value
                ? Verified(in before, in after,
                    "The global " + Name(action.Dial) + " is now " + action.Value + ".")
                : Fault(
                    SpellCompositionPreflight.VerificationFailed,
                    SpellCompositionNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed,
                    in before,
                    in after,
                    "The global " + Name(action.Dial) + " variable did not hold the requested value.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var after = CaptureBestEffort(native, player, action.Dial, in before);
            if (after.Current == action.Value)
                return Verified(in before, in after,
                    "The " + Name(action.Dial) + " setter threw after the requested value became observable.");
            return Fault(
                SpellCompositionPreflight.PostCommitFault,
                SpellCompositionNativeStage.Dial,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                "The " + Name(action.Dial) + " setter threw before the requested outcome was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static SpellCompositionState Capture(
        SpellCompositionNativeBindings native,
        object player,
        CastingDial dial)
    {
        var variable = ReadVariable(native, dial) ??
            throw new InvalidOperationException("Player " + Name(dial) + " variable was null.");
        return new SpellCompositionState(
            dial,
            native.ReadInt(variable),
            native.ReadInt(ReadMaximumVariable(native, player, dial)));
    }

    private static SpellCompositionState CaptureBestEffort(
        SpellCompositionNativeBindings native,
        object player,
        CastingDial dial,
        in SpellCompositionState fallback)
    {
        try { return Capture(native, player, dial); }
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
            "Casting dial faulted after " + stage + ": " + reason);
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
            reason = "The suite does not own the Casting-dial action family.";
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

    private static object? ReadVariable(
        SpellCompositionNativeBindings native,
        CastingDial dial) => dial == CastingDial.Output
            ? native.ReadOutputVariable()
            : native.ReadReserveVariable();

    private static object ReadMaximumVariable(
        SpellCompositionNativeBindings native,
        object player,
        CastingDial dial) => dial == CastingDial.Output
            ? native.ReadMaximumOutputVariable(player)
            : native.ReadMaximumReserveVariable(player);

    private static string Name(CastingDial dial) => dial == CastingDial.Output
        ? "Output Level"
        : "Reserve Level";
}
