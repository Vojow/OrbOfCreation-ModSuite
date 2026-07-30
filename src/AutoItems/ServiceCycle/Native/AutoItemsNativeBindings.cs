using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The complete reflected Scroll/Relic transaction schema. Only metadata is retained, and one
/// instance is validated for each game lifecycle before any item is resolved or mutated.
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
        Type countType,
        Type scalingType,
        Type instantBlockType,
        Type instantScriptType,
        Type requestType,
        Type optionsType,
        Type baseSelectionType,
        Type targetStructureType,
        FieldInfo families,
        FieldInfo canBeRandomized,
        FieldInfo onUseEffects,
        FieldInfo effectScripts,
        FieldInfo targetOptions,
        MethodInfo familyGuid,
        MethodInfo canFire,
        MethodInfo isVisible,
        MethodInfo selectAndFire,
        MethodInfo setRandomization,
        MethodInfo isRandomized,
        MethodInfo getQuantity,
        MethodInfo getQueued,
        MethodInfo canUseConsumable,
        MethodInfo strongest,
        MethodInfo strongestLevel,
        MethodInfo countScaling,
        MethodInfo getTargeting,
        MethodInfo getRandomList)
    {
        ConsumableType = consumableType;
        FamilyType = familyType;
        CountType = countType;
        ScalingType = scalingType;
        InstantBlockType = instantBlockType;
        InstantScriptType = instantScriptType;
        RequestType = requestType;
        OptionsType = optionsType;
        BaseSelectionType = baseSelectionType;
        TargetStructureType = targetStructureType;
        Families = families;
        CanBeRandomized = canBeRandomized;
        OnUseEffects = onUseEffects;
        EffectScripts = effectScripts;
        TargetOptions = targetOptions;
        FamilyGuid = familyGuid;
        CanFire = canFire;
        IsVisible = isVisible;
        SelectAndFire = selectAndFire;
        SetRandomization = setRandomization;
        IsRandomized = isRandomized;
        GetQuantity = getQuantity;
        GetQueued = getQueued;
        CanUseConsumable = canUseConsumable;
        Strongest = strongest;
        StrongestLevel = strongestLevel;
        CountScaling = countScaling;
        GetTargeting = getTargeting;
        GetRandomList = getRandomList;
    }

    internal Type ConsumableType { get; }
    internal Type FamilyType { get; }
    internal Type CountType { get; }
    internal Type ScalingType { get; }
    internal Type InstantBlockType { get; }
    internal Type InstantScriptType { get; }
    internal Type RequestType { get; }
    internal Type OptionsType { get; }
    internal Type BaseSelectionType { get; }
    internal Type TargetStructureType { get; }
    internal FieldInfo Families { get; }
    internal FieldInfo CanBeRandomized { get; }
    internal FieldInfo OnUseEffects { get; }
    internal FieldInfo EffectScripts { get; }
    internal FieldInfo TargetOptions { get; }
    internal MethodInfo FamilyGuid { get; }
    internal MethodInfo CanFire { get; }
    internal MethodInfo IsVisible { get; }
    internal MethodInfo SelectAndFire { get; }
    internal MethodInfo SetRandomization { get; }
    internal MethodInfo IsRandomized { get; }
    internal MethodInfo GetQuantity { get; }
    internal MethodInfo GetQueued { get; }
    internal MethodInfo CanUseConsumable { get; }
    internal MethodInfo Strongest { get; }
    internal MethodInfo StrongestLevel { get; }
    internal MethodInfo CountScaling { get; }
    internal MethodInfo GetTargeting { get; }
    internal MethodInfo GetRandomList { get; }

    internal static bool TryCreate(out AutoItemsNativeBindings? bindings, out string reason)
    {
        bindings = null;
        var consumable = ReflectionUtil.FindLoadedType("ConsumableSO");
        var family = ReflectionUtil.FindLoadedType("ConsumableTypeSO");
        var inventory = ReflectionUtil.FindLoadedType("Inventory");
        var count = ReflectionUtil.FindLoadedType("ConsumableCount");
        var scaling = ReflectionUtil.FindLoadedType("ScalingInfo");
        var block = ReflectionUtil.FindLoadedType("InstantEffectBlock");
        var script = ReflectionUtil.FindLoadedType("IInstantEffectScript");
        var request = ReflectionUtil.FindLoadedType("RequestTargetEffectScript");
        var options = ReflectionUtil.FindLoadedType("Targeting.TargetSelectOptions");
        var selection = ReflectionUtil.FindLoadedType("Targeting.BaseTargetSelection");
        var structure = ReflectionUtil.FindLoadedType("Targeting.TargetStructure");
        var targetable = ReflectionUtil.FindLoadedType("Targeting.ITargetable");
        if (consumable is null || family is null || inventory is null || count is null ||
            scaling is null || block is null || script is null || request is null ||
            options is null || selection is null || structure is null || targetable is null)
        {
            reason =
                "The complete Auto Items type set is unavailable: ConsumableSO, " +
                "ConsumableTypeSO, Inventory, ConsumableCount, ScalingInfo, InstantEffectBlock, " +
                "IInstantEffectScript, RequestTargetEffectScript, Targeting.TargetSelectOptions, " +
                "Targeting.BaseTargetSelection, Targeting.TargetStructure, and " +
                "Targeting.ITargetable are all required.";
            return false;
        }

        var families = consumable.GetField("consumableTypes", AnyInstance);
        var randomizable = consumable.GetField("canBeRandomized", AnyInstance);
        var onUse = consumable.GetField("onUseEffects", AnyInstance);
        var scripts = block.GetField("effectScripts", AnyInstance);
        var targetOptions = request.GetField("targetOptions", AnyInstance);
        var familyGuid = ExactMethod(family, "GetGuid", typeof(Guid), PublicInstance);
        var canFire = ExactMethod(consumable, "CanFire", typeof(bool), PublicInstance);
        var isVisible = ExactMethod(consumable, "IsVisible", typeof(bool), PublicInstance);
        var selectAndFire = ExactMethod(consumable, "SelectAndFire", typeof(void), PublicInstance);
        var setRandomization = ExactMethod(
            consumable, "SetRandomization", typeof(void), PublicInstance, typeof(bool));
        var isRandomized = ExactMethod(consumable, "IsRandomized", typeof(bool), PublicInstance);
        var getQuantity = ExactMethod(consumable, "GetQuantity", typeof(int), PublicInstance);
        var getQueued = ExactMethod(consumable, "GetQueued", typeof(int), PublicInstance);
        var canUse = ExactMethod(inventory, "CanUseConsumable", typeof(bool), PublicStatic);
        var strongest = ExactMethod(consumable, "GetStrongest", count, PublicInstance);
        var strongestLevel = ExactMethod(
            consumable, "GetStrongestLevel", typeof(int), PublicInstance);
        var countScaling = ExactMethod(
            consumable, "GetCountScalingInfo", scaling, PublicInstance, count);
        var getTargeting = ExactMethod(options, "GetTargeting", selection, PublicInstance);
        var getRandomList = ExactMethod(
            structure,
            "GetRandomList",
            typeof(List<>).MakeGenericType(targetable),
            PublicInstance,
            scaling);

        if (families is null || CollectionElementType(families.FieldType) != family)
            return Missing("ConsumableSO.consumableTypes : List<ConsumableTypeSO>", out reason);
        if (randomizable?.FieldType != typeof(bool))
            return Missing("ConsumableSO.canBeRandomized : Boolean", out reason);
        if (onUse is null || CollectionElementType(onUse.FieldType) != block)
            return Missing("ConsumableSO.onUseEffects : List<InstantEffectBlock>", out reason);
        if (scripts is null || CollectionElementType(scripts.FieldType) != script)
            return Missing("InstantEffectBlock.effectScripts : List<IInstantEffectScript>", out reason);
        if (targetOptions?.FieldType != options)
            return Missing(
                "RequestTargetEffectScript.targetOptions : Targeting.TargetSelectOptions",
                out reason);
        if (!script.IsAssignableFrom(request))
            return Missing("RequestTargetEffectScript : IInstantEffectScript", out reason);
        if (familyGuid is null) return Missing("ConsumableTypeSO.GetGuid() : Guid", out reason);
        if (canFire is null) return Missing("ConsumableSO.CanFire() : Boolean", out reason);
        if (isVisible is null) return Missing("ConsumableSO.IsVisible() : Boolean", out reason);
        if (selectAndFire is null)
            return Missing("ConsumableSO.SelectAndFire() : Void", out reason);
        if (setRandomization is null)
            return Missing("ConsumableSO.SetRandomization(Boolean) : Void", out reason);
        if (isRandomized is null)
            return Missing("ConsumableSO.IsRandomized() : Boolean", out reason);
        if (getQuantity is null)
            return Missing("ConsumableSO.GetQuantity() : Int32", out reason);
        if (getQueued is null)
            return Missing("ConsumableSO.GetQueued() : Int32", out reason);
        if (canUse is null)
            return Missing("Inventory.CanUseConsumable() : Boolean", out reason);
        if (strongest is null)
            return Missing("ConsumableSO.GetStrongest() : ConsumableCount", out reason);
        if (strongestLevel is null)
            return Missing("ConsumableSO.GetStrongestLevel() : Int32", out reason);
        if (countScaling is null)
            return Missing(
                "ConsumableSO.GetCountScalingInfo(ConsumableCount) : ScalingInfo",
                out reason);
        if (getTargeting is null)
            return Missing(
                "Targeting.TargetSelectOptions.GetTargeting() : Targeting.BaseTargetSelection",
                out reason);
        if (getRandomList is null)
            return Missing(
                "Targeting.TargetStructure.GetRandomList(ScalingInfo) : List<Targeting.ITargetable>",
                out reason);

        bindings = new AutoItemsNativeBindings(
            consumable,
            family,
            count,
            scaling,
            block,
            script,
            request,
            options,
            selection,
            structure,
            families,
            randomizable,
            onUse,
            scripts,
            targetOptions,
            familyGuid,
            canFire,
            isVisible,
            selectAndFire,
            setRandomization,
            isRandomized,
            getQuantity,
            getQueued,
            canUse,
            strongest,
            strongestLevel,
            countScaling,
            getTargeting,
            getRandomList);
        reason = string.Empty;
        return true;
    }

    private static bool Missing(string contract, out string reason)
    {
        reason = "The exact audited Auto Items binding is unavailable: " + contract + ".";
        return false;
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
                return candidate.GetGenericArguments()[0];
        }
        return null;
    }
}
