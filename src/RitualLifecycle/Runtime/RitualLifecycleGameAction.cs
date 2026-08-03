using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for the visible Ritual controls.</summary>
internal sealed class RitualLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private RitualLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal RitualLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal RitualLifecycleSubmission Submit(in RitualLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(RitualLifecyclePreflight.WrongThread,
                "Ritual controls are bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return Reject(RitualLifecyclePreflight.ContractUnavailable, _bindingFailure);

        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(RitualLifecyclePreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(RitualLifecyclePreflight.LifecycleReplaced,
                "The submitted game lifecycle is stale.");

        try
        {
            var resolution = _registry.Resolve(action.RitualId, native.RitualType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(RitualLifecyclePreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? "The ritual resolution became stale."
                        : resolution.Reason);
            var ritual = resolution.Value!;
            if (!native.IsDiscovered(ritual))
                return Reject(RitualLifecyclePreflight.NotDiscovered,
                    EntityIdentityFormatter.Format(action.RitualId) +
                    " has not been discovered.");

            var manager = native.Manager();
            var battle = native.BattleManager();
            if (manager is null || manager.GetType() != native.ManagerType ||
                battle is null || battle.GetType() != native.BattleManagerType)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "The Ritual or battle manager is not available in this scene.");
            var selected = native.SelectedVariable(manager);
            if (selected is null)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "The Ritual selection is not available in this scene.");
            if (native.IsInCombat(battle))
                return Reject(RitualLifecyclePreflight.BattleAlreadyActive,
                    "Ritual controls are unavailable while a ritual battle is active.");
            if (action.Kind is not RitualLifecycleActionKind.Select and
                not RitualLifecycleActionKind.Deselect and
                not RitualLifecycleActionKind.SetLevel and
                not RitualLifecycleActionKind.Activate and
                not RitualLifecycleActionKind.CancelDuration)
                return Reject(RitualLifecyclePreflight.ContractUnavailable,
                    "That ritual control is not available.");

            var isSelected = native.IsSelected(selected, ritual);
            switch (action.Kind)
            {
                case RitualLifecycleActionKind.Select when isSelected:
                    return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                        "This ritual is already selected.");
                case RitualLifecycleActionKind.Deselect when !isSelected:
                    return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                        "This ritual is not selected.");
                case RitualLifecycleActionKind.SetLevel:
                    if (!isSelected)
                        return Reject(RitualLifecyclePreflight.NotSelected,
                            "Select this ritual before changing its starting level.");
                    if (native.ForceLevel(ritual))
                        return Reject(RitualLifecyclePreflight.LevelLocked,
                            "This ritual fixes its starting level at " +
                            native.ForceLevelValue(ritual) + ".");
                    var maximum = Math.Max(native.MaximumSelectedLevel(ritual), 0);
                    if (action.Level < 0 || action.Level > maximum)
                        return Reject(RitualLifecyclePreflight.LevelOutOfRange,
                            "The ritual starting level must be between 0 and " + maximum + ".");
                    if (native.SelectedLevel(ritual) == action.Level)
                        return Reject(RitualLifecyclePreflight.AlreadyInRequestedState,
                            "The ritual starting level is already " + action.Level + ".");
                    break;
                case RitualLifecycleActionKind.Activate:
                    if (!isSelected)
                        return Reject(RitualLifecyclePreflight.NotSelected,
                            "Select this ritual before activating it.");
                    var cost = native.ActivationCost(ritual);
                    if (cost is null)
                        return Reject(RitualLifecyclePreflight.ContractUnavailable,
                            "The ritual activation price is unavailable.");
                    if (!native.HasEnough(cost))
                        return Unaffordable(native, cost);
                    break;
                case RitualLifecycleActionKind.CancelDuration:
                    if (!native.IsDurationRitual(ritual) || !native.IsDurationActive(ritual))
                        return Reject(RitualLifecyclePreflight.NoDurationEffect,
                            "This ritual has no active duration reward to cancel.");
                    break;
            }

            if (!_tryCaptureMutationPermit())
                return Reject(RitualLifecyclePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, manager, selected, ritual);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(RitualLifecyclePreflight.ContractUnavailable,
                "Ritual preflight failed before mutation: " +
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

    private static RitualLifecycleSubmission Execute(
        in RitualLifecycleAction action,
        RitualLifecycleNativeBindings native,
        object manager,
        object selected,
        object ritual)
    {
        var stage = RitualLifecycleNativeStage.NativeCallback;
        try
        {
            if (action.Kind is RitualLifecycleActionKind.Select or
                RitualLifecycleActionKind.Deselect)
            {
                native.ToggleSelected(selected, ritual);
            }
            else if (action.Kind == RitualLifecycleActionKind.SetLevel)
            {
                native.ChangeStartingLevel(ritual, action.Level);
            }
            else if (action.Kind == RitualLifecycleActionKind.Activate)
            {
                var cost = native.ActivationCost(ritual) ??
                    throw new InvalidOperationException(
                        "RitualSO.GetActivationCost returned null before payment");
                if (!native.IsSelected(selected, ritual))
                    return Reject(RitualLifecyclePreflight.NotSelected,
                        "The selected ritual changed before activation.");
                if (!native.HasEnough(cost)) return Unaffordable(native, cost);
                stage = RitualLifecycleNativeStage.Payment;
                native.PerformCost(cost);
                stage = RitualLifecycleNativeStage.NativeCallback;
                native.ActivateSelected(manager);
            }
            else if (action.Kind == RitualLifecycleActionKind.CancelDuration)
            {
                native.Cancel(ritual);
            }
            else
            {
                throw new InvalidOperationException("Unsupported ritual control.");
            }

            stage = RitualLifecycleNativeStage.Verification;
            return OutcomeObserved(in action, native, selected, ritual)
                ? Verified()
                : Fault(in action, RitualLifecyclePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The requested Ritual transition was not observable.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeObserved(in action, native, selected, ritual)) return Verified();
            return Fault(in action, RitualLifecyclePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native Ritual callback threw before the requested transition was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static bool OutcomeObserved(
        in RitualLifecycleAction action,
        RitualLifecycleNativeBindings native,
        object selected,
        object ritual) =>
        action.Kind switch
        {
            RitualLifecycleActionKind.Select => native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.Deselect => !native.IsSelected(selected, ritual),
            RitualLifecycleActionKind.SetLevel => native.SelectedLevel(ritual) == action.Level,
            RitualLifecycleActionKind.Activate => native.InBattle(ritual),
            RitualLifecycleActionKind.CancelDuration => !native.IsDurationActive(ritual),
            _ => false,
        };

    private static RitualLifecycleSubmission Unaffordable(
        RitualLifecycleNativeBindings native,
        object cost)
    {
        var rows = native.CostEntries(cost);
        if (rows is null)
            return Reject(RitualLifecyclePreflight.ContractUnavailable,
                "The ritual activation price has no readable resource rows.");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.CostResource(row);
            if (resource is null) continue;
            var amount = native.CostValue(row);
            if (!native.HasResourceAmount(resource, amount))
                return Reject(RitualLifecyclePreflight.Unaffordable,
                    EntityIdentityFormatter.Format(native.ResourceGuid(resource)) +
                    " is short for this ritual activation.");
        }
        return Reject(RitualLifecyclePreflight.ContractUnavailable,
            "The game refused this ritual price without identifying a short resource.");
    }

    private static RitualLifecycleSubmission Reject(
        RitualLifecyclePreflight preflight,
        string reason) => RitualLifecycleSubmission.Reject(preflight, reason);

    private static RitualLifecycleSubmission Verified() =>
        new(RitualLifecyclePreflight.Proceeded,
            RitualLifecycleNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested Ritual transition is visible.");

    private static RitualLifecycleSubmission Fault(
        in RitualLifecycleAction action,
        RitualLifecyclePreflight preflight,
        RitualLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Ritual " + stage + " failed on " +
            EntityIdentityFormatter.Format(action.RitualId) + ": " + reason);

    private void BindLifecycle()
    {
        if (RitualLifecycleNativeBindings.TryCreate(
                out var bindings,
                out var reason,
                _resolveType,
                _includeContract))
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
