using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single Scroll/Relic GameAction: lifecycle-scoped complete bindings, live preflights,
/// one native transaction, and exact before/after evidence.
/// </summary>
internal sealed class AutoItemsConsumableUseGameAction : IDisposable
{
    private static readonly object[] EnableRandomizationArguments = { true };

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readMutationPermitFailure;
    private AutoItemsNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoItemsConsumableUseGameAction(
        TypedRegistryResolver registryResolver,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readMutationPermitFailure)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readMutationPermitFailure = readMutationPermitFailure ??
            throw new ArgumentNullException(nameof(readMutationPermitFailure));
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal string QuarantineReason => _quarantineReason;

    internal AutoItemsSubmission Submit(in AutoItemsCycleAction action)
    {
        if (_quarantineReason.Length != 0)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Auto Items binding set is unavailable."
                    : _bindingFailure);

        var resolution = _registryResolver.Resolve(action.ItemId, native.ConsumableType);
        if (!resolution.IsResolved)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        if (!HasExpectedFamily(item, action.Family, native, out var reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.FamilyChanged, reason);
        if (!InvokeBool(native.IsVisible, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotVisible,
                $"ConsumableSO.IsVisible() refused {action.ItemId:D}.");
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            native.CanBeRandomized.GetValue(item) is not true)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.RandomizationUnavailable,
                $"Scroll {action.ItemId:D} no longer has ConsumableSO.canBeRandomized=true.");
        }
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            !AutoItemsScrollTargetPreflight.TryHasValidTarget(
                item,
                action.PlannedLevel,
                native,
                out reason))
        {
            return AutoItemsSubmission.Reject(AutoItemsPreflight.TargetUnavailable, reason);
        }
        if (!InvokeBool(native.CanUseConsumable, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "Inventory.CanUseConsumable() refused because native consumable preparation is busy.");
        if (!InvokeBool(native.CanFire, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.CanFireRefused,
                $"ConsumableSO.CanFire() refused live {action.Family} {action.ItemId:D}.");
        if (!TryCaptureMutationPermit(out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.MutationPermitUnavailable,
                reason);
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.MultiBuyUnavailable, reason);

        var itemId = action.ItemId;
        var family = action.Family;
        NativeMutationEvidence<ItemState> evidence;
        using (multiBuy)
        {
            evidence = NativeMutationVerifier.Execute(
                "Auto Items consumable use",
                itemId.ToString("D"),
                "one item leaves stock and one item enters the native preparation queue",
                () => Capture(item, native),
                () => Mutate(item, family, native),
                (before, after) =>
                    after.Quantity == before.Quantity - 1 &&
                    after.Queued == before.Queued + 1 &&
                    (family != AutoItemsConsumableFamily.Scroll || after.Randomized));
        }

        var callOutcome = new NativeMutationCallOutcome(
            evidence.MutationWasAttempted
                ? action.Family == AutoItemsConsumableFamily.Scroll ? 2 : 1
                : 0,
            evidence.MutationWasAttempted ? 1 : 0,
            evidence.IsVerified ? 1 : 0);
        var preflight = AutoItemsPreflight.Proceeded;
        var failureReason = string.Empty;
        if (evidence.MutationWasAttempted && !evidence.IsVerified)
        {
            _quarantineReason =
                "Auto Items is quarantined for this lifecycle after an ambiguous consumable " +
                $"mutation on {action.ItemId:D}: {evidence.Detail}";
            preflight = AutoItemsPreflight.Quarantined;
            failureReason = _quarantineReason;
        }
        else if (!evidence.IsVerified)
        {
            preflight = AutoItemsPreflight.ContractUnavailable;
            failureReason =
                "Auto Items could not capture consumable before-state evidence for " +
                $"{action.ItemId:D}: {evidence.Detail}";
        }
        return new AutoItemsSubmission(
            preflight,
            evidence.Outcome,
            callOutcome,
            evidence.IsVerified
                ? $"Verified {action.Family} {action.ItemId:D}: stock -1, queue +1."
                : failureReason);
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

    private void BindLifecycle()
    {
        if (AutoItemsNativeBindings.TryCreate(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private bool TryCaptureMutationPermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readMutationPermitFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Auto Items no longer owns ConsumableUse and NativeMultiBuyOverride.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            reason =
                "Auto Items could not capture its consumable-use ownership permit: " +
                ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool HasExpectedFamily(
        object item,
        AutoItemsConsumableFamily expected,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (native.Families.GetValue(item) is not IEnumerable families)
        {
            reason = "ConsumableSO.consumableTypes was unavailable on the live item.";
            return false;
        }

        var actual = AutoItemsConsumableFamily.Unknown;
        var supportedCount = 0;
        foreach (var entry in families)
        {
            if (entry is null || entry.GetType() != native.FamilyType)
            {
                reason =
                    "ConsumableSO.consumableTypes contained a non-ConsumableTypeSO value.";
                return false;
            }
            var candidate = AutoItemsConsumableFamilies.FromTypeId(
                Invoke<Guid>(native.FamilyGuid, entry));
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            actual = candidate;
            supportedCount++;
        }
        if (supportedCount == 1 && actual == expected)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"Expected exactly one live {expected} family for the planned consumable, but " +
            $"observed {supportedCount} supported families and resolved {actual}.";
        return false;
    }

    private static void Mutate(
        object item,
        AutoItemsConsumableFamily family,
        AutoItemsNativeBindings native)
    {
        if (family == AutoItemsConsumableFamily.Scroll)
        {
            native.SetRandomization.Invoke(item, EnableRandomizationArguments);
            if (!InvokeBool(native.IsRandomized, item))
                throw new InvalidOperationException(
                    "ConsumableSO.SetRandomization(true) was not confirmed by IsRandomized().");
        }
        native.SelectAndFire.Invoke(item, Array.Empty<object>());
    }

    private static ItemState Capture(object item, AutoItemsNativeBindings native) =>
        new(
            Invoke<int>(native.GetQuantity, item),
            Invoke<int>(native.GetQueued, item),
            InvokeBool(native.IsRandomized, item));

    private static bool InvokeBool(MethodInfo method, object? target) =>
        Invoke<bool>(method, target);

    private static T Invoke<T>(MethodInfo method, object? target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");

    private readonly struct ItemState
    {
        internal ItemState(int quantity, int queued, bool randomized)
        {
            Quantity = quantity;
            Queued = queued;
            Randomized = randomized;
        }

        internal int Quantity { get; }
        internal int Queued { get; }
        internal bool Randomized { get; }
    }
}
