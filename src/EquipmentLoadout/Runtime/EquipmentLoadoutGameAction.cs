using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped, Unity-main-thread equipment loadout boundary.</summary>
internal sealed class EquipmentLoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private EquipmentLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal EquipmentLoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(_readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal EquipmentLoadoutSubmission Submit(in EquipmentLoadoutAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.WrongThread,
                "Equipment loadout is bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LifecycleReplaced,
                "The submitted lifecycle is stale.");

        try
        {
            var resolution = _registry.Resolve(action.TargetId, native.EquipmentType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.IdentityUnavailable,
                    resolution.IsResolved ? "The equipment resolution became stale." : resolution.Reason);
            var target = resolution.Value!;
            if (!native.IsCreated(target))
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.NotCreated,
                    EntityIdentityFormatter.Format(action.TargetId) + " has not been created.");
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                    "EquipmentManager.instance was unavailable.");
            var list = native.EquippedList(manager);
            var kind = native.ReadEquipmentType(target);
            var multiBuyValue = native.MultiBuy();
            var cost = native.UsageCost(target);
            if (list is null || kind is null || multiBuyValue is null || cost is null)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                    "The native equipment decision graph returned a null member.");
            var before = Capture(native, list, target, kind, cost, multiBuyValue);
            var requested = RequestedAmount(action.Kind, in before);
            if (action.Kind == EquipmentLoadoutActionKind.Equip)
            {
                if (before.EquippedStacks >= before.MaximumStacks)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.AlreadyInRequestedState,
                        "The artifact is already equipped at its maximum stack level.");
                if (before.EquippedStacks == 0 && before.UsedSlots >= before.MaximumSlots)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LoadoutFull,
                        "The equipment loadout has no open global slot.");
                if (before.EquippedStacks == 0 && before.TypeUsedSlots >= before.TypeMaximumSlots)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.EquipmentTypeFull,
                        "The artifact's equipment type has no open slot.");
                if (!before.UsageAffordable || before.MaximumAffordableStacks <= 0)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.UsageUnaffordable,
                        "The native usage-cost evaluators refused this equipment increase.");
            }
            else if (before.EquippedStacks == 0)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.AlreadyInRequestedState,
                    "The artifact is not equipped.");
            if (before.MultiBuy <= 0 || requested <= 0)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.MultiBuyUnavailable,
                    "The native multi-buy value permits no equipment stack transition.");
            if (!_tryCaptureMutationPermit())
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, manager, list, target, kind, cost, multiBuyValue,
                in before, requested);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                "Equipment loadout preflight failed before mutation: " + exception.GetBaseException().Message);
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

    private EquipmentLoadoutSubmission Execute(in EquipmentLoadoutAction action,
        EquipmentLoadoutNativeBindings native, object manager, object list, object target,
        object kind, object cost, object multiBuyValue, in EquipmentLoadoutState before, int requested)
    {
        var stage = EquipmentLoadoutNativeStage.NativeCallback;
        try
        {
            if (action.Kind == EquipmentLoadoutActionKind.Equip) native.Equip(manager, target);
            else native.Unequip(manager, target);
            stage = EquipmentLoadoutNativeStage.Verification;
            var after = Capture(native, list, target, kind, cost, multiBuyValue);
            var receipt = new EquipmentLoadoutReceipt(true, action.Kind, requested, in before, in after);
            return OutcomeMatches(action.Kind, in before, in after, requested)
                ? Verified(in receipt)
                : Fault(in action, EquipmentLoadoutPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed, in receipt,
                    "The exact requested artifact stack did not make the audited native transition.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            EquipmentLoadoutState after;
            var evidenceAvailable = true;
            try { after = Capture(native, list, target, kind, cost, multiBuyValue); }
            catch (Exception) { after = default; evidenceAvailable = false; }
            var receipt = new EquipmentLoadoutReceipt(evidenceAvailable, action.Kind, requested, in before, in after);
            if (evidenceAvailable && OutcomeMatches(action.Kind, in before, in after, requested))
                return Verified(in receipt);
            return Fault(in action, EquipmentLoadoutPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, in receipt,
                "The native equipment callback threw before the requested target outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static EquipmentLoadoutState Capture(EquipmentLoadoutNativeBindings native,
        object list, object target, object kind, object cost, object multiBuyValue) =>
        new(Math.Max(native.Stacks(list, target), 0), Math.Max(native.MaximumStacks(target), 0),
            Math.Max(native.AsInt(multiBuyValue), 0),
            native.Values(list)?.Count ?? throw new InvalidOperationException("Equipment list values were unavailable."),
            Math.Max(native.MaximumSlots(list), 0), Math.Max(native.TypeCount(list, kind), 0),
            Math.Max(native.TypeMaximum(kind), 0), native.HasEnough(cost),
            Math.Max(BigDouble.Floor(native.MaximumTimes(cost)).ToInt(), 0));

    private static int RequestedAmount(EquipmentLoadoutActionKind kind, in EquipmentLoadoutState state) =>
        kind == EquipmentLoadoutActionKind.Equip
            ? Math.Min(state.MultiBuy, Math.Min(Math.Max(state.MaximumStacks - state.EquippedStacks, 0),
                state.MaximumAffordableStacks))
            : Math.Min(state.MultiBuy, state.EquippedStacks);

    private static bool OutcomeMatches(EquipmentLoadoutActionKind kind,
        in EquipmentLoadoutState before, in EquipmentLoadoutState after, int requested) =>
        kind == EquipmentLoadoutActionKind.Equip
            ? after.EquippedStacks == checked(before.EquippedStacks + requested)
            : after.EquippedStacks == checked(before.EquippedStacks - requested);

    private static EquipmentLoadoutSubmission Verified(in EquipmentLoadoutReceipt receipt) =>
        new(EquipmentLoadoutPreflight.Proceeded, EquipmentLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1), in receipt,
            "Verified the exact requested artifact stack transition; usage reservations are evidence only.");

    private static EquipmentLoadoutSubmission Fault(in EquipmentLoadoutAction action,
        EquipmentLoadoutPreflight preflight, EquipmentLoadoutNativeStage stage,
        NativeMutationOutcome outcome, in EquipmentLoadoutReceipt receipt, string reason)
    {
        var exactReason = "Equipment loadout " + stage + " failed on " +
            EntityIdentityFormatter.Format(action.TargetId) + ": " + reason;
        return new EquipmentLoadoutSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), in receipt, exactReason);
    }

    private void BindLifecycle()
    {
        if (EquipmentLoadoutNativeBindings.TryCreate(out var bindings, out var reason, _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or TargetInvocationException or OverflowException;
}
