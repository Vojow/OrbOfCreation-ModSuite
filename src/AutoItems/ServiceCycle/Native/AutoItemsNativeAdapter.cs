using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoItemsNativeAdapter : IDisposable
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _captureMutationPermit;
    private readonly AutoItemsTemporaryActivationTracker _temporaryActivations;
    private readonly HashSet<Guid> _itemQuarantine = new();
    private Type? _consumableType;
    private Type? _consumableTypeEntryType;
    private FieldInfo? _consumableTypes;
    private FieldInfo? _allConsumables;
    private FieldInfo? _consumableUsages;
    private FieldInfo? _canBeRandomizedField;
    private FieldInfo? _hasDurationField;
    private FieldInfo? _durationBaseField;
    private MethodInfo? _typeGuid;
    private MethodInfo? _canFire;
    private MethodInfo? _isVisible;
    private MethodInfo? _selectAndFire;
    private MethodInfo? _setRandomization;
    private MethodInfo? _isRandomized;
    private MethodInfo? _getQuantity;
    private MethodInfo? _getQueued;
    private MethodInfo? _canUseConsumable;
    private bool _initialized;
    private string? _quarantineReason;

    internal AutoItemsNativeAdapter(
        TypedRegistryResolver registryResolver,
        Func<bool> captureMutationPermit,
        AutoItemsTemporaryActivationTracker temporaryActivations)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        _captureMutationPermit = captureMutationPermit ??
            throw new ArgumentNullException(nameof(captureMutationPermit));
        _temporaryActivations = temporaryActivations ??
            throw new ArgumentNullException(nameof(temporaryActivations));
    }

    internal AutoItemsSubmission Submit(in AutoItemsCycleAction action)
    {
        var family = action.Family;
        if (_itemQuarantine.Contains(action.ItemId))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                "This exact temporary item is quarantined for the current lifecycle.");
        if (_quarantineReason is not null)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _quarantineReason);
        if (!TryInitialize(out var reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.ContractUnavailable, reason);

        var resolution = _registryResolver.Resolve(action.ItemId, _consumableType!);
        if (!resolution.IsResolved)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        if (!HasExpectedFamily(item, family, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.FamilyChanged, reason);
        if (!InvokeBool(_isVisible!, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotAdmissible,
                "The live consumable is not visible.");
        if (family == AutoItemsConsumableFamily.Scroll &&
            _canBeRandomizedField!.GetValue(item) is not true)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.RandomizationUnavailable,
                "The live Scroll does not support native random targeting.");
        if (IsTemporary(family))
        {
            if (_hasDurationField!.GetValue(item) is not true ||
                _durationBaseField!.GetValue(item) is not double durationBase ||
                durationBase <= 0d ||
                double.IsNaN(durationBase) ||
                double.IsInfinity(durationBase))
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.NotAdmissible,
                    "The live temporary item does not expose a finite positive duration.");
            if (AnyTemporaryUsage())
                return AutoItemsSubmission.Reject(
                    AutoItemsPreflight.TemporaryEffectPresent,
                    "A native Fruit or Potion usage is already pending or active.");
        }
        if (!InvokeBool(_canUseConsumable!, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "Inventory.CanUseConsumable() refused while another consumable was preparing.");
        if (!InvokeBool(_canFire!, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotAdmissible,
                "ConsumableSO.CanFire() refused the live item.");

        if (!CaptureMutationPermit())
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.MutationPermitUnavailable,
                "Auto Items no longer owns the complete consumable-use transaction.");
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.MultiBuyUnavailable, reason);

        NativeMutationEvidence<ItemState> evidence;
        var temporary = IsTemporary(family);
        using (multiBuy)
        {
            evidence = NativeMutationVerifier.Execute(
                "Auto Items",
                action.ItemId.ToString("D"),
                "one item leaves stock and one prepared usage enters the native queue",
                () => Capture(item),
                () =>
                {
                    if (family == AutoItemsConsumableFamily.Scroll)
                    {
                        _setRandomization!.Invoke(item, new object[] { true });
                        if (!InvokeBool(_isRandomized!, item))
                            throw new InvalidOperationException(
                                "The Scroll did not accept native random targeting.");
                    }
                    _selectAndFire!.Invoke(item, Array.Empty<object>());
                },
                (before, after) =>
                    after.Quantity == before.Quantity - 1 &&
                    after.Queued == before.Queued + 1 &&
                    (!temporary || after.Usages == before.Usages + 1) &&
                    (family != AutoItemsConsumableFamily.Scroll || after.Randomized));
        }

        var attemptedCalls = evidence.MutationWasAttempted
            ? family == AutoItemsConsumableFamily.Scroll ? 2 : 1
            : 0;
        var callOutcome = new NativeMutationCallOutcome(
            attemptedCalls,
            attemptedCalls,
            evidence.IsVerified ? 1 : 0);
        if (evidence.MutationWasAttempted && !evidence.IsVerified)
        {
            if (temporary) _itemQuarantine.Add(action.ItemId);
            else
                _quarantineReason =
                    $"An attempted Auto Items mutation was ambiguous: {evidence.Detail}";
        }
        if (temporary && evidence.IsVerified)
            _temporaryActivations.RecordSubmitted(action.ItemId, action.CollectedAtFrame);
        return new AutoItemsSubmission(
            AutoItemsPreflight.Proceeded,
            evidence.Outcome,
            callOutcome,
            evidence.Detail);
    }

    internal void InvalidateLifecycle()
    {
        _initialized = false;
        _consumableType = null;
        _consumableTypeEntryType = null;
        _consumableTypes = null;
        _allConsumables = null;
        _consumableUsages = null;
        _canBeRandomizedField = null;
        _hasDurationField = null;
        _durationBaseField = null;
        _typeGuid = null;
        _canFire = null;
        _isVisible = null;
        _selectAndFire = null;
        _setRandomization = null;
        _isRandomized = null;
        _getQuantity = null;
        _getQueued = null;
        _canUseConsumable = null;
        _quarantineReason = null;
        _itemQuarantine.Clear();
    }

    public void Dispose() => InvalidateLifecycle();

    private bool TryInitialize(out string reason)
    {
        if (_initialized)
        {
            reason = string.Empty;
            return true;
        }

        _consumableType = ReflectionUtil.FindLoadedType("ConsumableSO");
        var inventoryType = ReflectionUtil.FindLoadedType("Inventory");
        if (_consumableType is null || inventoryType is null)
        {
            reason = "ConsumableSO or Inventory is not loaded.";
            return false;
        }

        _consumableTypes = _consumableType.GetField("consumableTypes", AnyInstance);
        _allConsumables = _consumableType.GetField("All", PublicStatic);
        _consumableUsages = _consumableType.GetField("consumableUsages", AnyInstance);
        _canBeRandomizedField = _consumableType.GetField("canBeRandomized", AnyInstance);
        _hasDurationField = _consumableType.GetField("hasDuration", AnyInstance);
        _durationBaseField = _consumableType.GetField("durationBase", AnyInstance);
        _consumableTypeEntryType = CollectionElementType(_consumableTypes?.FieldType);
        _typeGuid = ExactMethod(
            _consumableTypeEntryType, "GetGuid", typeof(Guid), PublicInstance);
        _canFire = ExactMethod(_consumableType, "CanFire", typeof(bool), PublicInstance);
        _isVisible = ExactMethod(_consumableType, "IsVisible", typeof(bool), PublicInstance);
        _selectAndFire = ExactMethod(_consumableType, "SelectAndFire", typeof(void), PublicInstance);
        _setRandomization = ExactMethod(
            _consumableType, "SetRandomization", typeof(void), PublicInstance, typeof(bool));
        _isRandomized = ExactMethod(_consumableType, "IsRandomized", typeof(bool), PublicInstance);
        _getQuantity = ExactMethod(_consumableType, "GetQuantity", typeof(int), PublicInstance);
        _getQueued = ExactMethod(_consumableType, "GetQueued", typeof(int), PublicInstance);
        _canUseConsumable = ExactMethod(
            inventoryType, "CanUseConsumable", typeof(bool), PublicStatic);
        if (_consumableTypes is null ||
            _allConsumables?.FieldType.GetInterface(nameof(IEnumerable)) is null ||
            _consumableUsages?.FieldType.GetInterface(nameof(IEnumerable)) is null ||
            _canBeRandomizedField?.FieldType != typeof(bool) ||
            _hasDurationField?.FieldType != typeof(bool) ||
            _durationBaseField?.FieldType != typeof(double) ||
            _consumableTypes.FieldType.GetInterface(nameof(IEnumerable)) is null ||
            _typeGuid is null ||
            _canFire is null ||
            _isVisible is null ||
            _selectAndFire is null ||
            _setRandomization is null ||
            _isRandomized is null ||
            _getQuantity is null ||
            _getQueued is null ||
            _canUseConsumable is null)
        {
            reason = "The exact audited Auto Items native contracts are unavailable.";
            InvalidateLifecycle();
            return false;
        }

        _initialized = true;
        reason = string.Empty;
        return true;
    }

    private bool HasExpectedFamily(
        object item,
        AutoItemsConsumableFamily expected,
        out string reason)
    {
        if (_consumableTypes!.GetValue(item) is not IEnumerable types)
        {
            reason = "The live consumable family list is unavailable.";
            return false;
        }

        var supported = AutoItemsConsumableFamily.Unknown;
        var supportedCount = 0;
        foreach (var entry in types)
        {
            if (entry is null || entry.GetType() != _consumableTypeEntryType)
            {
                reason = "The live consumable family list changed type.";
                return false;
            }
            var id = (Guid)_typeGuid!.Invoke(entry, Array.Empty<object>());
            var family = KnownFamily(id);
            if (family == AutoItemsConsumableFamily.Unknown) continue;
            supported = family;
            supportedCount++;
        }

        if (supportedCount == 1 && supported == expected)
        {
            reason = string.Empty;
            return true;
        }
        reason = $"Expected exactly one {expected} family but observed {supportedCount} supported families.";
        return false;
    }

    private bool CaptureMutationPermit()
    {
        try { return _captureMutationPermit(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    private ItemState Capture(object item) =>
        new(
            (int)_getQuantity!.Invoke(item, Array.Empty<object>()),
            (int)_getQueued!.Invoke(item, Array.Empty<object>()),
            InvokeBool(_isRandomized!, item),
            CollectionCount(_consumableUsages!.GetValue(item)));

    private bool AnyTemporaryUsage()
    {
        if (_allConsumables!.GetValue(null) is not IEnumerable all)
            throw new InvalidOperationException("ConsumableSO.All was unavailable.");
        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != _consumableType)
                throw new InvalidOperationException("ConsumableSO.All changed element type.");
            if (!TryGetSingleSupportedFamily(candidate, out var family) || !IsTemporary(family))
                continue;
            if (CollectionCount(_consumableUsages!.GetValue(candidate)) > 0) return true;
        }
        return false;
    }

    private bool TryGetSingleSupportedFamily(object item, out AutoItemsConsumableFamily family)
    {
        family = AutoItemsConsumableFamily.Unknown;
        if (_consumableTypes!.GetValue(item) is not IEnumerable types)
            throw new InvalidOperationException("A live consumable family list was unavailable.");
        var count = 0;
        foreach (var entry in types)
        {
            if (entry is null || entry.GetType() != _consumableTypeEntryType)
                throw new InvalidOperationException("A live consumable family list changed type.");
            var candidate = KnownFamily((Guid)_typeGuid!.Invoke(entry, Array.Empty<object>()));
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            family = candidate;
            count++;
        }
        if (count > 1)
            throw new InvalidOperationException(
                "A live consumable had more than one supported Auto Items family.");
        return count == 1;
    }

    private static int CollectionCount(object? value) =>
        value is ICollection collection
            ? collection.Count
            : throw new InvalidOperationException("A native consumable usage list was unavailable.");

    private static bool IsTemporary(AutoItemsConsumableFamily family) =>
        family is AutoItemsConsumableFamily.Fruit or AutoItemsConsumableFamily.Potion;

    private static bool InvokeBool(MethodInfo method, object? target) =>
        method.Invoke(target, Array.Empty<object>()) is true;

    private static MethodInfo? ExactMethod(
        Type? type,
        string name,
        Type returnType,
        BindingFlags flags,
        params Type[] parameters)
    {
        var method = type?.GetMethod(name, flags, null, parameters, null);
        return method?.ReturnType == returnType && method.IsStatic == flags.HasFlag(BindingFlags.Static)
            ? method
            : null;
    }

    private static Type? CollectionElementType(Type? type) =>
        type?.IsGenericType == true ? type.GetGenericArguments()[0] : null;

    private static AutoItemsConsumableFamily KnownFamily(Guid id)
    {
        if (id == KnownEntities.ConsumableFruitType.Uuid) return AutoItemsConsumableFamily.Fruit;
        if (id == KnownEntities.ConsumablePotionType.Uuid) return AutoItemsConsumableFamily.Potion;
        if (id == KnownEntities.ConsumableRelicType.Uuid) return AutoItemsConsumableFamily.Relic;
        if (id == KnownEntities.ConsumableScrollType.Uuid) return AutoItemsConsumableFamily.Scroll;
        return AutoItemsConsumableFamily.Unknown;
    }

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
