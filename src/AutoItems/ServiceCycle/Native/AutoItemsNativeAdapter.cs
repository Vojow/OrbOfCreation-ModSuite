using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoItemsNativeAdapter : IDisposable
{
    private static readonly object[] EnableRandomizationArguments = { true };

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly AutoScribeIdentityProfile? _autoScribeIdentityProfile;
    private readonly HashSet<Guid> _itemQuarantine = new();
    private AutoItemsNativeBindings? _bindings;
    private string? _quarantineReason;

    internal AutoItemsNativeAdapter(
        TypedRegistryResolver registryResolver,
        Func<bool> tryCaptureMutationPermit,
        AutoScribeIdentityProfile? autoScribeIdentityProfile = null)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _autoScribeIdentityProfile = autoScribeIdentityProfile;
    }

    internal AutoItemsSubmission Submit(in AutoItemsCycleAction action)
    {
        if (_itemQuarantine.Contains(action.ItemId))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                "This exact temporary item is quarantined for the current lifecycle.");
        if (_quarantineReason is not null)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _quarantineReason);
        if (!TryGetBindings(out var native, out var reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.ContractUnavailable, reason);

        var resolution = _registryResolver.Resolve(action.ItemId, native.ConsumableType);
        if (!resolution.IsResolved)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        if (!HasExpectedFamily(item, action.Family, native, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.FamilyChanged, reason);
        if (!InvokeBool(native.IsVisible, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotAdmissible,
                "The live consumable is not visible.");
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            native.CanBeRandomized.GetValue(item) is not true)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.RandomizationUnavailable,
                "The live Scroll does not support native random targeting.");
        }
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            _autoScribeIdentityProfile is not null &&
            _autoScribeIdentityProfile.TryFindByScroll(action.ItemId, out _) &&
            !AutoItemsScrollTargetPreflight.TryHasValidTarget(
                item,
                action.PlannedLevel,
                out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TargetUnavailable,
                reason);
        }

        var temporary = AutoItemsConsumableFamilies.IsTemporary(action.Family);
        if (temporary)
        {
            if (!HasFinitePositiveDuration(item, native))
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.NotAdmissible,
                    "The live temporary item does not expose a finite positive duration.");
            if (!HasSafeTemporaryCosts(item, native))
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.TemporaryCostChanged,
                    "The live temporary item no longer has toxicity-only native cost vectors.");
        }
        if (AnyTemporaryUsage(native))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryEffectPresent,
                "A native temporary-item usage is already pending or active.");
        if (!InvokeBool(native.CanUseConsumable, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "Inventory.CanUseConsumable() refused while another consumable was preparing.");
        if (!InvokeBool(native.CanFire, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotAdmissible,
                "ConsumableSO.CanFire() refused the live item.");
        if (!TryCaptureMutationPermit())
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.MutationPermitUnavailable,
                "Auto Items no longer owns the complete consumable-use transaction.");
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.MultiBuyUnavailable, reason);

        var itemId = action.ItemId;
        var family = action.Family;
        NativeMutationEvidence<ItemState> evidence;
        using (multiBuy)
        {
            evidence = NativeMutationVerifier.Execute(
                "Auto Items",
                itemId.ToString("D"),
                "one item leaves stock and one prepared usage enters the native queue",
                () => Capture(item, native),
                () => Mutate(item, family, native),
                (before, after) =>
                    after.Quantity == before.Quantity - 1 &&
                    after.Queued == before.Queued + 1 &&
                    (!temporary || after.Usages == before.Usages + 1) &&
                    (family != AutoItemsConsumableFamily.Scroll || after.Randomized));
        }

        var attemptedCalls = evidence.MutationWasAttempted
            ? action.Family == AutoItemsConsumableFamily.Scroll ? 2 : 1
            : 0;
        var callOutcome = new NativeMutationCallOutcome(
            attemptedCalls,
            attemptedCalls,
            evidence.IsVerified ? 1 : 0);
        QuarantineAmbiguousMutation(in action, temporary, in evidence);
        return new AutoItemsSubmission(
            AutoItemsPreflight.Proceeded,
            evidence.Outcome,
            callOutcome,
            evidence.Detail);
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _quarantineReason = null;
        _itemQuarantine.Clear();
    }

    public void Dispose() => InvalidateLifecycle();

    private bool TryGetBindings(
        out AutoItemsNativeBindings bindings,
        out string reason)
    {
        if (_bindings is not null)
        {
            bindings = _bindings;
            reason = string.Empty;
            return true;
        }

        if (!AutoItemsNativeBindings.TryCreate(out var created, out reason))
        {
            bindings = null!;
            return false;
        }

        _bindings = created;
        bindings = created!;
        return true;
    }

    private static bool HasExpectedFamily(
        object item,
        AutoItemsConsumableFamily expected,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryReadSingleSupportedFamily(item, native, out var actual, out var count))
        {
            reason = "The live consumable family list changed shape.";
            return false;
        }
        if (count == 1 && actual == expected)
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Expected exactly one {expected} family but observed {count} supported families.";
        return false;
    }

    private static bool HasFinitePositiveDuration(
        object item,
        AutoItemsNativeBindings native) =>
        native.HasDuration.GetValue(item) is true &&
        native.DurationBase.GetValue(item) is double durationBase &&
        durationBase > 0d &&
        !double.IsNaN(durationBase) &&
        !double.IsInfinity(durationBase);

    private static bool HasSafeTemporaryCosts(
        object item,
        AutoItemsNativeBindings native) =>
        HasToxicityOnlyCosts(native.ConsumeCost.GetValue(item), native, requireToxicity: true) &&
        HasToxicityOnlyCosts(native.UsageCost.GetValue(item), native, requireToxicity: false);

    private static bool HasToxicityOnlyCosts(
        object? costList,
        AutoItemsNativeBindings native,
        bool requireToxicity)
    {
        if (costList is null || native.Costs.GetValue(costList) is not IEnumerable costs)
            return false;

        var hasToxicity = false;
        foreach (var entry in costs)
        {
            if (entry is null || entry.GetType() != native.CostEntryType) return false;
            var resource = native.CostResource.GetValue(entry);
            if (resource is null || resource.GetType() != native.ResourceType) return false;
            if (Invoke<Guid>(native.ResourceGuid, resource) !=
                KnownEntities.PotionToxicity.Uuid)
            {
                return false;
            }
            if (native.CostAmount.GetValue(entry) is not BigDouble amount ||
                BigDouble.IsNaN(amount) ||
                BigDouble.IsInfinity(amount) ||
                amount.CompareTo(BigDouble.Zero) < 0)
            {
                return false;
            }
            hasToxicity = true;
        }
        return hasToxicity || !requireToxicity;
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
                    "The Scroll did not accept native random targeting.");
        }
        native.SelectAndFire.Invoke(item, Array.Empty<object>());
    }

    private static ItemState Capture(object item, AutoItemsNativeBindings native) =>
        new(
            Invoke<int>(native.GetQuantity, item),
            Invoke<int>(native.GetQueued, item),
            InvokeBool(native.IsRandomized, item),
            CollectionCount(native.Usages.GetValue(item)));

    private static bool AnyTemporaryUsage(AutoItemsNativeBindings native)
    {
        if (native.AllConsumables.GetValue(null) is not IEnumerable all)
            throw new InvalidOperationException("ConsumableSO.All was unavailable.");
        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != native.ConsumableType)
                throw new InvalidOperationException("ConsumableSO.All changed element type.");
            if (!TryReadSingleSupportedFamily(
                    candidate,
                    native,
                    out var family,
                    out var supportedCount))
            {
                throw new InvalidOperationException(
                    "A live consumable family list changed shape.");
            }
            if (supportedCount != 1 ||
                !AutoItemsConsumableFamilies.IsTemporary(family))
            {
                continue;
            }
            if (CollectionCount(native.Usages.GetValue(candidate)) > 0) return true;
        }
        return false;
    }

    private static bool TryReadSingleSupportedFamily(
        object item,
        AutoItemsNativeBindings native,
        out AutoItemsConsumableFamily family,
        out int supportedCount)
    {
        family = AutoItemsConsumableFamily.Unknown;
        supportedCount = 0;
        if (native.Families.GetValue(item) is not IEnumerable families) return false;
        foreach (var entry in families)
        {
            if (entry is null || entry.GetType() != native.FamilyType) return false;
            var candidate = AutoItemsConsumableFamilies.FromTypeId(
                Invoke<Guid>(native.FamilyGuid, entry));
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            family = candidate;
            supportedCount++;
        }
        return true;
    }

    private bool TryCaptureMutationPermit()
    {
        try
        {
            return _tryCaptureMutationPermit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private void QuarantineAmbiguousMutation(
        in AutoItemsCycleAction action,
        bool temporary,
        in NativeMutationEvidence<ItemState> evidence)
    {
        if (!evidence.MutationWasAttempted || evidence.IsVerified) return;
        if (temporary)
        {
            _itemQuarantine.Add(action.ItemId);
            return;
        }
        _quarantineReason =
            $"An attempted Auto Items mutation was ambiguous: {evidence.Detail}";
    }

    private static int CollectionCount(object? value) =>
        value is ICollection collection
            ? collection.Count
            : throw new InvalidOperationException("A native consumable usage list was unavailable.");

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
