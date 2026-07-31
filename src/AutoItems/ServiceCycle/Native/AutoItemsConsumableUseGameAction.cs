using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single consumable-use GameAction: lifecycle-scoped complete bindings, live preflights,
/// one native transaction, and exact before/after evidence.
/// </summary>
internal sealed class AutoItemsConsumableUseGameAction : IDisposable
{
    private static readonly object[] EnableRandomizationArguments = { true };

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readMutationPermitFailure;
    private readonly Dictionary<Guid, string> _temporaryQuarantine = new();
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
        if (_temporaryQuarantine.TryGetValue(action.ItemId, out var exactQuarantineReason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                exactQuarantineReason);
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

        var temporary = AutoItemsConsumableFamilies.IsTemporary(action.Family);
        if (temporary &&
            !TryHasFinitePositiveDuration(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryDurationChanged,
                reason);
        }
        if (temporary &&
            !TryHasSafeTemporaryCosts(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryCostChanged,
                reason);
        }
        if (!TryAnyTemporaryUsage(native, out var temporaryUsagePresent, out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                reason);
        if (temporaryUsagePresent)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryEffectPresent,
                reason);
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
                temporary
                    ? "one item leaves stock, one enters the native queue, and one temporary usage appears"
                    : "one item leaves stock and one item enters the native preparation queue",
                () => Capture(item, native),
                () => Mutate(item, family, native),
                (before, after) =>
                    after.Quantity == before.Quantity - 1 &&
                    after.Queued == before.Queued + 1 &&
                    (!temporary || after.Usages == before.Usages + 1) &&
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
            if (temporary)
            {
                failureReason =
                    $"Temporary item {action.ItemId:D} is quarantined for this lifecycle after " +
                    $"an ambiguous consumable mutation: {evidence.Detail}";
                _temporaryQuarantine[action.ItemId] = failureReason;
            }
            else
            {
                _quarantineReason =
                    "Auto Items is quarantined for this lifecycle after an ambiguous consumable " +
                    $"mutation on {action.ItemId:D}: {evidence.Detail}";
                failureReason = _quarantineReason;
            }
            preflight = AutoItemsPreflight.Quarantined;
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
                ? $"Verified {action.Family} {action.ItemId:D}: stock -1, queue +1" +
                  (temporary ? ", usage +1." : ".")
                : failureReason);
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
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
        if (!TryReadSupportedFamilyEvidence(
                item,
                native,
                out var families,
                out reason))
        {
            return false;
        }
        if (families.TryResolveExecutionFamily(out var actual) && actual == expected)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"Expected live execution family {expected} for the planned consumable, but " +
            $"observed supported memberships [{families.Describe()}] and resolved {actual}.";
        return false;
    }

    private static bool TryHasFinitePositiveDuration(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (native.HasDuration.GetValue(item) is not true)
        {
            reason = $"Temporary item {itemId:D} no longer has ConsumableSO.hasDuration=true.";
            return false;
        }
        if (native.DurationBase.GetValue(item) is not double durationBase)
        {
            reason = $"Temporary item {itemId:D} did not expose ConsumableSO.durationBase as Double.";
            return false;
        }
        if (durationBase <= 0d || double.IsNaN(durationBase) || double.IsInfinity(durationBase))
        {
            reason =
                $"Temporary item {itemId:D} has non-finite or non-positive " +
                $"ConsumableSO.durationBase={durationBase}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryHasSafeTemporaryCosts(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryHasToxicityOnlyCosts(
                native.ConsumeCost.GetValue(item),
                "ConsumableSO.consumeCost",
                itemId,
                native,
                requireToxicity: true,
                out reason))
        {
            return false;
        }
        return TryHasToxicityOnlyCosts(
            native.UsageCost.GetValue(item),
            "ConsumableSO.usageCost",
            itemId,
            native,
            requireToxicity: false,
            out reason);
    }

    private static bool TryHasToxicityOnlyCosts(
        object? costList,
        string category,
        Guid itemId,
        AutoItemsNativeBindings native,
        bool requireToxicity,
        out string reason)
    {
        if (costList is null)
        {
            reason = $"Temporary item {itemId:D} has null {category}.";
            return false;
        }
        if (native.Costs.GetValue(costList) is not IEnumerable costs)
        {
            reason = $"Temporary item {itemId:D} has unreadable {category}.costs.";
            return false;
        }

        var hasToxicity = false;
        var index = 0;
        foreach (var entry in costs)
        {
            if (entry is null || entry.GetType() != native.CostEntryType)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}] is not the exact " +
                    "ResourceTuple type.";
                return false;
            }
            var resource = native.CostResource.GetValue(entry);
            if (resource is null || resource.GetType() != native.ResourceType)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}].resource is not " +
                    "the exact ResourceSO type.";
                return false;
            }
            var resourceId = Invoke<Guid>(native.ResourceGuid, resource);
            if (resourceId != KnownEntities.PotionToxicity.Uuid)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}] names extra resource " +
                    $"{resourceId:D}; only Potion Toxicity is permitted.";
                return false;
            }
            if (native.CostAmount.GetValue(entry) is not BigDouble amount ||
                BigDouble.IsNaN(amount) ||
                BigDouble.IsInfinity(amount) ||
                amount.CompareTo(BigDouble.Zero) < 0)
            {
                reason =
                    $"Temporary item {itemId:D} {category}.costs[{index}].valueBig is invalid.";
                return false;
            }
            hasToxicity = true;
            index++;
        }

        if (requireToxicity && !hasToxicity)
        {
            reason = $"Temporary item {itemId:D} {category} has no Potion Toxicity entry.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryAnyTemporaryUsage(
        AutoItemsNativeBindings native,
        out bool present,
        out string reason)
    {
        present = false;
        if (native.AllConsumables.GetValue(null) is not IEnumerable all)
        {
            reason = "ConsumableSO.All was unavailable while checking temporary usage exclusion.";
            return false;
        }

        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != native.ConsumableType)
            {
                reason =
                    "ConsumableSO.All contained a value that was not the exact ConsumableSO type.";
                return false;
            }
            if (!TryReadSupportedFamilyEvidence(
                    candidate,
                    native,
                    out var families,
                    out reason))
            {
                return false;
            }
            if (families.Count == 0) continue;
            if (!families.TryResolveExecutionFamily(out var family))
            {
                reason =
                    "A live consumable has incoherent supported family memberships [" +
                    families.Describe() + "].";
                return false;
            }
            if (!AutoItemsConsumableFamilies.IsTemporary(family)) continue;
            if (native.Usages.GetValue(candidate) is not ICollection usages)
            {
                reason =
                    $"Temporary-family consumable family={family}, " +
                    $"supportedFamilies=[{families.Describe()}] did not expose " +
                    "ConsumableSO.consumableUsages as ICollection.";
                return false;
            }
            if (usages.Count <= 0) continue;
            present = true;
            reason =
                "A native temporary-item usage is already pending or active: " +
                $"family={family}, supportedFamilies=[{families.Describe()}], usages={usages.Count}.";
            return true;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryReadSupportedFamilyEvidence(
        object item,
        AutoItemsNativeBindings native,
        out AutoItemsConsumableFamilySet families,
        out string reason)
    {
        families = new AutoItemsConsumableFamilySet();
        if (native.Families.GetValue(item) is not IList entries)
        {
            reason = "ConsumableSO.consumableTypes was unavailable on the live item.";
            return false;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != native.FamilyType)
            {
                reason =
                    "ConsumableSO.consumableTypes contained a non-ConsumableTypeSO value.";
                return false;
            }
            var familyId = Invoke<Guid>(native.FamilyGuid, entry);
            if (familyId == Guid.Empty)
            {
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                if (Invoke<Guid>(native.FamilyGuid, entries[previous]!) != familyId) continue;
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            var candidate = AutoItemsConsumableFamilies.FromTypeId(familyId);
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            if (families.TryAdd(candidate)) continue;
            reason =
                $"ConsumableSO.consumableTypes repeated supported family {candidate}.";
            return false;
        }
        reason = string.Empty;
        return true;
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
            InvokeBool(native.IsRandomized, item),
            native.Usages.GetValue(item) is ICollection usages
                ? usages.Count
                : throw new InvalidOperationException(
                    "ConsumableSO.consumableUsages was unavailable during mutation evidence capture."));

    private static bool InvokeBool(MethodInfo method, object? target) =>
        Invoke<bool>(method, target);

    private static T Invoke<T>(MethodInfo method, object? target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");

    private readonly struct ItemState
    {
        internal ItemState(int quantity, int queued, bool randomized, int usages)
        {
            Quantity = quantity;
            Queued = queued;
            Randomized = randomized;
            Usages = usages;
        }

        internal int Quantity { get; }
        internal int Queued { get; }
        internal bool Randomized { get; }
        internal int Usages { get; }
    }
}
