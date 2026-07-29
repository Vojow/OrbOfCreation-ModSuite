using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Validates the complete native surface used by the display-only temporary-item catalog before
/// any entry is read. It holds metadata and the current registry enumerable, never native items.
/// </summary>
internal sealed class AutoItemsTemporaryItemCatalogBindings
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private AutoItemsTemporaryItemCatalogBindings(
        Type consumableType,
        Type familyType,
        Type resourceType,
        Type costEntryType,
        IEnumerable entries,
        FieldInfo families,
        FieldInfo durationBase,
        FieldInfo consumeCost,
        FieldInfo costs,
        FieldInfo resource,
        FieldInfo valueBig,
        MethodInfo itemGuid,
        MethodInfo familyGuid,
        MethodInfo resourceGuid,
        MethodInfo getName,
        MethodInfo isVisible,
        MethodInfo getQuantity)
    {
        ConsumableType = consumableType;
        FamilyType = familyType;
        ResourceType = resourceType;
        CostEntryType = costEntryType;
        Entries = entries;
        Families = families;
        DurationBase = durationBase;
        ConsumeCost = consumeCost;
        Costs = costs;
        Resource = resource;
        ValueBig = valueBig;
        ItemGuid = itemGuid;
        FamilyGuid = familyGuid;
        ResourceGuid = resourceGuid;
        GetName = getName;
        IsVisible = isVisible;
        GetQuantity = getQuantity;
    }

    internal Type ConsumableType { get; }
    internal Type FamilyType { get; }
    internal Type ResourceType { get; }
    internal Type CostEntryType { get; }
    internal IEnumerable Entries { get; }
    internal FieldInfo Families { get; }
    internal FieldInfo DurationBase { get; }
    internal FieldInfo ConsumeCost { get; }
    internal FieldInfo Costs { get; }
    internal FieldInfo Resource { get; }
    internal FieldInfo ValueBig { get; }
    internal MethodInfo ItemGuid { get; }
    internal MethodInfo FamilyGuid { get; }
    internal MethodInfo ResourceGuid { get; }
    internal MethodInfo GetName { get; }
    internal MethodInfo IsVisible { get; }
    internal MethodInfo GetQuantity { get; }

    internal static bool TryCreate(
        out AutoItemsTemporaryItemCatalogBindings? bindings,
        out string unavailableReason)
    {
        bindings = null;
        unavailableReason = "The exact native item-picker contracts are unavailable.";
        var consumableType = ReflectionUtil.FindLoadedType("ConsumableSO");
        var familyType = ReflectionUtil.FindLoadedType("ConsumableTypeSO");
        var tooltipableType = ReflectionUtil.FindLoadedType("TooltipableObject");
        var resourceType = ReflectionUtil.FindLoadedType("ResourceSO");
        if (consumableType is null ||
            familyType is null ||
            tooltipableType is null ||
            resourceType is null)
        {
            unavailableReason = "The native consumable catalog is not loaded.";
            return false;
        }

        var all = consumableType.GetField("All", PublicStatic);
        var families = consumableType.GetField("consumableTypes", AnyInstance);
        var durationBase = consumableType.GetField("durationBase", AnyInstance);
        var consumeCost = consumableType.GetField("consumeCost", AnyInstance);
        var costs = consumeCost?.FieldType.GetField("costs", AnyInstance);
        var costEntryType = CollectionElementType(costs?.FieldType);
        var resource = costEntryType?.GetField("resource", AnyInstance);
        var valueBig = costEntryType?.GetField("valueBig", AnyInstance);
        var itemGuid = ExactMethod(
            consumableType, "GetGuid", typeof(Guid), PublicInstance);
        var familyGuid = ExactMethod(
            familyType, "GetGuid", typeof(Guid), PublicInstance);
        var resourceGuid = ExactMethod(
            resourceType, "GetGuid", typeof(Guid), PublicInstance);
        var getName = ExactMethod(
            tooltipableType, "GetName", typeof(string), PublicInstance);
        var isVisible = ExactMethod(
            consumableType, "IsVisible", typeof(bool), PublicInstance);
        var getQuantity = ExactMethod(
            consumableType, "GetQuantity", typeof(int), PublicInstance);

        if (all?.GetValue(null) is not IEnumerable entries ||
            families is null ||
            !typeof(IEnumerable).IsAssignableFrom(families.FieldType) ||
            durationBase?.FieldType != typeof(double) ||
            consumeCost is null ||
            costs is null ||
            !typeof(IEnumerable).IsAssignableFrom(costs.FieldType) ||
            costEntryType is null ||
            resource?.FieldType != resourceType ||
            valueBig is null ||
            itemGuid is null ||
            familyGuid is null ||
            resourceGuid is null ||
            getName is null ||
            isVisible is null ||
            getQuantity is null)
        {
            return false;
        }

        bindings = new AutoItemsTemporaryItemCatalogBindings(
            consumableType,
            familyType,
            resourceType,
            costEntryType,
            entries,
            families,
            durationBase,
            consumeCost,
            costs,
            resource,
            valueBig,
            itemGuid,
            familyGuid,
            resourceGuid,
            getName,
            isVisible,
            getQuantity);
        return true;
    }

    private static MethodInfo? ExactMethod(
        Type type,
        string name,
        Type returnType,
        BindingFlags flags)
    {
        var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
        return method?.ReturnType == returnType && !method.IsStatic ? method : null;
    }

    private static Type? CollectionElementType(Type? type)
    {
        if (type is null) return null;
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
