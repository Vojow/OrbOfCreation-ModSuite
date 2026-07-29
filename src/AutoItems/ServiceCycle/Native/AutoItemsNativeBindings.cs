using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Validates the entire reflected mutation surface as one unit. The adapter caches only this
/// metadata; lifecycle invalidation discards it before any later native object is resolved.
/// </summary>
internal sealed class AutoItemsNativeBindings
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private AutoItemsNativeBindings(
        Type consumableType,
        Type familyType,
        Type resourceType,
        Type costEntryType,
        FieldInfo families,
        FieldInfo allConsumables,
        FieldInfo usages,
        FieldInfo canBeRandomized,
        FieldInfo hasDuration,
        FieldInfo durationBase,
        FieldInfo consumeCost,
        FieldInfo usageCost,
        FieldInfo costs,
        FieldInfo costResource,
        FieldInfo costAmount,
        MethodInfo familyGuid,
        MethodInfo resourceGuid,
        MethodInfo canFire,
        MethodInfo isVisible,
        MethodInfo selectAndFire,
        MethodInfo setRandomization,
        MethodInfo isRandomized,
        MethodInfo getQuantity,
        MethodInfo getQueued,
        MethodInfo canUseConsumable)
    {
        ConsumableType = consumableType;
        FamilyType = familyType;
        ResourceType = resourceType;
        CostEntryType = costEntryType;
        Families = families;
        AllConsumables = allConsumables;
        Usages = usages;
        CanBeRandomized = canBeRandomized;
        HasDuration = hasDuration;
        DurationBase = durationBase;
        ConsumeCost = consumeCost;
        UsageCost = usageCost;
        Costs = costs;
        CostResource = costResource;
        CostAmount = costAmount;
        FamilyGuid = familyGuid;
        ResourceGuid = resourceGuid;
        CanFire = canFire;
        IsVisible = isVisible;
        SelectAndFire = selectAndFire;
        SetRandomization = setRandomization;
        IsRandomized = isRandomized;
        GetQuantity = getQuantity;
        GetQueued = getQueued;
        CanUseConsumable = canUseConsumable;
    }

    internal Type ConsumableType { get; }
    internal Type FamilyType { get; }
    internal Type ResourceType { get; }
    internal Type CostEntryType { get; }
    internal FieldInfo Families { get; }
    internal FieldInfo AllConsumables { get; }
    internal FieldInfo Usages { get; }
    internal FieldInfo CanBeRandomized { get; }
    internal FieldInfo HasDuration { get; }
    internal FieldInfo DurationBase { get; }
    internal FieldInfo ConsumeCost { get; }
    internal FieldInfo UsageCost { get; }
    internal FieldInfo Costs { get; }
    internal FieldInfo CostResource { get; }
    internal FieldInfo CostAmount { get; }
    internal MethodInfo FamilyGuid { get; }
    internal MethodInfo ResourceGuid { get; }
    internal MethodInfo CanFire { get; }
    internal MethodInfo IsVisible { get; }
    internal MethodInfo SelectAndFire { get; }
    internal MethodInfo SetRandomization { get; }
    internal MethodInfo IsRandomized { get; }
    internal MethodInfo GetQuantity { get; }
    internal MethodInfo GetQueued { get; }
    internal MethodInfo CanUseConsumable { get; }

    internal static bool TryCreate(
        out AutoItemsNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        reason = "The exact audited Auto Items native contracts are unavailable.";

        var consumableType = ReflectionUtil.FindLoadedType("ConsumableSO");
        var familyType = ReflectionUtil.FindLoadedType("ConsumableTypeSO");
        var resourceType = ReflectionUtil.FindLoadedType("ResourceSO");
        var inventoryType = ReflectionUtil.FindLoadedType("Inventory");
        if (consumableType is null ||
            familyType is null ||
            resourceType is null ||
            inventoryType is null)
        {
            reason =
                "ConsumableSO, ConsumableTypeSO, ResourceSO, or Inventory is not loaded.";
            return false;
        }

        var families = consumableType.GetField("consumableTypes", AnyInstance);
        var allConsumables = consumableType.GetField("All", PublicStatic);
        var usages = consumableType.GetField("consumableUsages", AnyInstance);
        var canBeRandomized = consumableType.GetField("canBeRandomized", AnyInstance);
        var hasDuration = consumableType.GetField("hasDuration", AnyInstance);
        var durationBase = consumableType.GetField("durationBase", AnyInstance);
        var consumeCost = consumableType.GetField("consumeCost", AnyInstance);
        var usageCost = consumableType.GetField("usageCost", AnyInstance);
        var costs = consumeCost?.FieldType.GetField("costs", AnyInstance);
        var costEntryType = costs is null
            ? null
            : CollectionElementType(costs.FieldType);
        var costResource = costEntryType?.GetField("resource", AnyInstance);
        var costAmount = costEntryType?.GetField("valueBig", AnyInstance);
        var familyGuid = ExactMethod(
            familyType,
            "GetGuid",
            typeof(Guid),
            PublicInstance);
        var resourceGuid = ExactMethod(
            resourceType,
            "GetGuid",
            typeof(Guid),
            PublicInstance);
        var canFire = ExactMethod(
            consumableType,
            "CanFire",
            typeof(bool),
            PublicInstance);
        var isVisible = ExactMethod(
            consumableType,
            "IsVisible",
            typeof(bool),
            PublicInstance);
        var selectAndFire = ExactMethod(
            consumableType,
            "SelectAndFire",
            typeof(void),
            PublicInstance);
        var setRandomization = ExactMethod(
            consumableType,
            "SetRandomization",
            typeof(void),
            PublicInstance,
            typeof(bool));
        var isRandomized = ExactMethod(
            consumableType,
            "IsRandomized",
            typeof(bool),
            PublicInstance);
        var getQuantity = ExactMethod(
            consumableType,
            "GetQuantity",
            typeof(int),
            PublicInstance);
        var getQueued = ExactMethod(
            consumableType,
            "GetQueued",
            typeof(int),
            PublicInstance);
        var canUseConsumable = ExactMethod(
            inventoryType,
            "CanUseConsumable",
            typeof(bool),
            PublicStatic);

        if (families is null ||
            CollectionElementType(families.FieldType) != familyType ||
            allConsumables is null ||
            !typeof(IEnumerable).IsAssignableFrom(allConsumables.FieldType) ||
            CollectionElementType(allConsumables.FieldType) != consumableType ||
            usages is null ||
            !typeof(ICollection).IsAssignableFrom(usages.FieldType) ||
            canBeRandomized?.FieldType != typeof(bool) ||
            hasDuration?.FieldType != typeof(bool) ||
            durationBase?.FieldType != typeof(double) ||
            consumeCost is null ||
            usageCost?.FieldType != consumeCost.FieldType ||
            costs is null ||
            !typeof(IEnumerable).IsAssignableFrom(costs.FieldType) ||
            costEntryType is null ||
            costResource?.FieldType != resourceType ||
            costAmount?.FieldType != typeof(BigDouble) ||
            familyGuid is null ||
            resourceGuid is null ||
            canFire is null ||
            isVisible is null ||
            selectAndFire is null ||
            setRandomization is null ||
            isRandomized is null ||
            getQuantity is null ||
            getQueued is null ||
            canUseConsumable is null)
        {
            return false;
        }

        bindings = new AutoItemsNativeBindings(
            consumableType,
            familyType,
            resourceType,
            costEntryType,
            families,
            allConsumables,
            usages,
            canBeRandomized,
            hasDuration,
            durationBase,
            consumeCost,
            usageCost,
            costs,
            costResource,
            costAmount,
            familyGuid,
            resourceGuid,
            canFire,
            isVisible,
            selectAndFire,
            setRandomization,
            isRandomized,
            getQuantity,
            getQueued,
            canUseConsumable);
        reason = string.Empty;
        return true;
    }

    private static MethodInfo? ExactMethod(
        Type type,
        string name,
        Type returnType,
        BindingFlags flags,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        return method?.ReturnType == returnType &&
               method.IsStatic == flags.HasFlag(BindingFlags.Static)
            ? method
            : null;
    }

    private static Type? CollectionElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return type.GetGenericArguments()[0];
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }
        return null;
    }
}
