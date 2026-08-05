using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the native Back to Menu control.</summary>
internal sealed class ReturnToMenuGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string> _readScene;
    private readonly Func<Type, object[]> _findLoadedObjects;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private ReturnToMenuNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal ReturnToMenuGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string> readScene,
        Func<Type, object[]> findLoadedObjects,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _readScene = readScene ?? throw new ArgumentNullException(nameof(readScene));
        _findLoadedObjects = findLoadedObjects ?? throw new ArgumentNullException(nameof(findLoadedObjects));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal ReturnToMenuSubmission Submit(in ReturnToMenuAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(ReturnToMenuPreflight.WrongThread,
                "Back to Menu is bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return Reject(ReturnToMenuPreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(ReturnToMenuPreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(ReturnToMenuPreflight.LifecycleReplaced,
                "The submitted game lifecycle is stale.");

        try
        {
            var scene = _readScene();
            if (!string.Equals(scene, "Main", StringComparison.Ordinal))
                return Reject(ReturnToMenuPreflight.WrongScene,
                    "Back to Menu is available while playing; the current scene is " + scene + ".");
            var flash = native.ScreenFlash();
            if (flash is null)
                return Reject(ReturnToMenuPreflight.ContractUnavailable,
                    "The game's scene-transition screen is unavailable.");
            if (native.FlashActive(flash))
                return Reject(ReturnToMenuPreflight.TransitionInProgress,
                    "The game is already changing screens.");
            var buttons = _findLoadedObjects(native.ButtonType) ?? Array.Empty<object>();
            var live = new List<object>(buttons.Length);
            for (var index = 0; index < buttons.Length; index++)
            {
                var candidate = buttons[index];
                if (candidate is not null && native.ButtonType.IsInstanceOfType(candidate) &&
                    native.ControlLive(candidate))
                    live.Add(candidate);
            }
            if (live.Count != 1)
            {
                return Reject(ReturnToMenuPreflight.ControlUnavailable,
                    live.Count == 0
                        ? "The game has no visible, interactable Back to Menu control."
                        : "The game has more than one visible, interactable Back to Menu control: " +
                          string.Join(", ", live.ConvertAll(value => native.ControlName(value))) + ".");
            }
            if (!_tryCaptureMutationPermit())
                return Reject(ReturnToMenuPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(native, live[0], flash);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(ReturnToMenuPreflight.ContractUnavailable,
                "Back to Menu preflight failed before transition: " +
                exception.GetBaseException().Message);
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

    private static ReturnToMenuSubmission Execute(
        ReturnToMenuNativeBindings native,
        object button,
        object flash)
    {
        var stage = ReturnToMenuNativeStage.NativeCallback;
        try
        {
            native.BackToMenu(button);
            stage = ReturnToMenuNativeStage.Verification;
            return native.FlashActive(flash)
                ? Verified()
                : Fault(ReturnToMenuPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The game did not start its screen transition.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (native.FlashActive(flash)) return Verified();
            return Fault(ReturnToMenuPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native Back to Menu callback threw before the screen transition started: " +
                exception.GetBaseException().Message);
        }
    }

    private static ReturnToMenuSubmission Reject(
        ReturnToMenuPreflight preflight,
        string reason) => ReturnToMenuSubmission.Reject(preflight, reason);

    private static ReturnToMenuSubmission Verified() =>
        new(ReturnToMenuPreflight.Proceeded, ReturnToMenuNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The game accepted the return to its Start screen.");

    private static ReturnToMenuSubmission Fault(
        ReturnToMenuPreflight preflight,
        ReturnToMenuNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0), reason);

    private void BindLifecycle()
    {
        if (ReturnToMenuNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}
