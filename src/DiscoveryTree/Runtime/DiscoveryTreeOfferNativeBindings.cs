using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for Discovery Tree offers. Reflection is confined to construction;
/// execution uses compiled delegates only, and a missing member disables the whole capability.
/// </summary>
internal sealed class DiscoveryTreeOfferNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "discovery-tree-offer-tree-type",
        "discovery-tree-offer-mode-type",
        "discovery-tree-offer-guid-container-type",
        "discovery-tree-offer-discoverable-type",
        "discovery-tree-offer-has-guid-type",
        "discovery-tree-offer-resource-cost-type",
        "discovery-tree-offer-resource-tuple-type",
        "discovery-tree-offer-resource-type",
        "discovery-tree-offer-all-field",
        "discovery-tree-offer-mode-field",
        "discovery-tree-offer-action-time-field",
        "discovery-tree-offer-rerolls-field",
        "discovery-tree-offer-used-rerolls-field",
        "discovery-tree-offer-current-choices-field",
        "discovery-tree-offer-next-exclusions-field",
        "discovery-tree-offer-selected-field",
        "discovery-tree-offer-total-count-field",
        "discovery-tree-offer-pool-count-field",
        "discovery-tree-offer-tree-identity-method",
        "discovery-tree-offer-guid-value-method",
        "discovery-tree-offer-visible-method",
        "discovery-tree-offer-idle-method",
        "discovery-tree-offer-crafting-method",
        "discovery-tree-offer-choice-method",
        "discovery-tree-offer-remaining-method",
        "discovery-tree-offer-immediate-required-method",
        "discovery-tree-offer-next-cost-method",
        "discovery-tree-offer-resolve-item-method",
        "discovery-tree-offer-initiate-method",
        "discovery-tree-offer-select-method",
        "discovery-tree-offer-confirm-method",
        "discovery-tree-offer-reroll-method",
        "discovery-tree-offer-item-identity-method",
        "discovery-tree-offer-item-discovered-method",
        "discovery-tree-offer-item-required-method",
        "discovery-tree-offer-cost-entries-method",
        "discovery-tree-offer-cost-enough-method",
        "discovery-tree-offer-cost-perform-method",
        "discovery-tree-offer-tuple-resource-field",
        "discovery-tree-offer-tuple-value-method",
        "discovery-tree-offer-resource-identity-method",
        "discovery-tree-offer-resource-quantity-method",
        "discovery-tree-offer-max-rerolls-method",
    };

    private DiscoveryTreeOfferNativeBindings(
        Type treeType,
        Type itemType,
        Type costType,
        Type tupleType,
        Type resourceType,
        Func<IList> readTrees,
        Func<object, Guid> readTreeIdentity,
        Func<object, int> readMode,
        Func<object, BigDouble> readActionTime,
        Func<object, int> readRerolls,
        Func<object, bool> readUsedRerolls,
        Func<object, IList> readCurrentChoices,
        Func<object, IList> readNextExclusions,
        Func<object, object> readSelected,
        Func<object, Guid> readGuid,
        Func<object, int> readTotalDiscovered,
        Func<object, int> readPoolDiscovered,
        Func<object, bool> isVisible,
        Func<object, bool> isIdle,
        Func<object, bool> isCrafting,
        Func<object, bool> isChoice,
        Func<object, bool> hasRemainingDiscoveries,
        Func<object, bool> hasImmediateRequired,
        Func<object, int> getMaxRerolls,
        Func<object, object> getNextCost,
        Func<object, Guid, object?> getItem,
        Action<object> initiate,
        Action<object, Guid> select,
        Action<object> confirm,
        Action<object> reroll,
        Func<object, Guid> readItemIdentity,
        Func<object, bool> isItemDiscovered,
        Func<object, bool> isItemRequired,
        Func<object, IList> getCostEntries,
        Func<object, bool> hasEnough,
        Action<object> performCost,
        Func<object, object> readTupleResource,
        Func<object, BigDouble> readTupleValue,
        Func<object, Guid> readResourceIdentity,
        Func<object, BigDouble> readResourceQuantity)
    {
        TreeType = treeType;
        ItemType = itemType;
        CostType = costType;
        TupleType = tupleType;
        ResourceType = resourceType;
        ReadTrees = readTrees;
        ReadTreeIdentity = readTreeIdentity;
        ReadMode = readMode;
        ReadActionTime = readActionTime;
        ReadRerolls = readRerolls;
        ReadUsedRerolls = readUsedRerolls;
        ReadCurrentChoices = readCurrentChoices;
        ReadNextExclusions = readNextExclusions;
        ReadSelected = readSelected;
        ReadGuid = readGuid;
        ReadTotalDiscovered = readTotalDiscovered;
        ReadPoolDiscovered = readPoolDiscovered;
        IsVisible = isVisible;
        IsIdle = isIdle;
        IsCrafting = isCrafting;
        IsChoice = isChoice;
        HasRemainingDiscoveries = hasRemainingDiscoveries;
        HasImmediateRequired = hasImmediateRequired;
        GetMaxRerolls = getMaxRerolls;
        GetNextCost = getNextCost;
        GetItem = getItem;
        Initiate = initiate;
        Select = select;
        Confirm = confirm;
        Reroll = reroll;
        ReadItemIdentity = readItemIdentity;
        IsItemDiscovered = isItemDiscovered;
        IsItemRequired = isItemRequired;
        GetCostEntries = getCostEntries;
        HasEnough = hasEnough;
        PerformCost = performCost;
        ReadTupleResource = readTupleResource;
        ReadTupleValue = readTupleValue;
        ReadResourceIdentity = readResourceIdentity;
        ReadResourceQuantity = readResourceQuantity;
    }

    internal Type TreeType { get; }
    internal Type ItemType { get; }
    internal Type CostType { get; }
    internal Type TupleType { get; }
    internal Type ResourceType { get; }
    internal Func<IList> ReadTrees { get; }
    internal Func<object, Guid> ReadTreeIdentity { get; }
    internal Func<object, int> ReadMode { get; }
    internal Func<object, BigDouble> ReadActionTime { get; }
    internal Func<object, int> ReadRerolls { get; }
    internal Func<object, bool> ReadUsedRerolls { get; }
    internal Func<object, IList> ReadCurrentChoices { get; }
    internal Func<object, IList> ReadNextExclusions { get; }
    internal Func<object, object> ReadSelected { get; }
    internal Func<object, Guid> ReadGuid { get; }
    internal Func<object, int> ReadTotalDiscovered { get; }
    internal Func<object, int> ReadPoolDiscovered { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> IsIdle { get; }
    internal Func<object, bool> IsCrafting { get; }
    internal Func<object, bool> IsChoice { get; }
    internal Func<object, bool> HasRemainingDiscoveries { get; }
    internal Func<object, bool> HasImmediateRequired { get; }
    internal Func<object, int> GetMaxRerolls { get; }
    internal Func<object, object> GetNextCost { get; }
    internal Func<object, Guid, object?> GetItem { get; }
    internal Action<object> Initiate { get; }
    internal Action<object, Guid> Select { get; }
    internal Action<object> Confirm { get; }
    internal Action<object> Reroll { get; }
    internal Func<object, Guid> ReadItemIdentity { get; }
    internal Func<object, bool> IsItemDiscovered { get; }
    internal Func<object, bool> IsItemRequired { get; }
    internal Func<object, IList> GetCostEntries { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    internal Func<object, object> ReadTupleResource { get; }
    internal Func<object, BigDouble> ReadTupleValue { get; }
    internal Func<object, Guid> ReadResourceIdentity { get; }
    internal Func<object, BigDouble> ReadResourceQuantity { get; }

    internal static bool TryCreate(
        out DiscoveryTreeOfferNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(string contract, string name)
            {
                Require(contract, includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable.");
            }
            FieldInfo F(string contract, Type type, string name, Type fieldType, bool isStatic = false)
            {
                Require(contract, includeContract);
                var field = type.GetField(name, isStatic ? Static : Instance);
                if (field is null || field.FieldType != fieldType || field.IsStatic != isStatic)
                    throw new InvalidOperationException($"{type.Name}.{name} : {fieldType.Name} was unavailable.");
                return field;
            }
            MethodInfo M(string contract, Type type, string name, Type result, params Type[] parameters)
            {
                Require(contract, includeContract);
                var method = type.GetMethod(name, Instance, null, parameters, null);
                if (method is null || method.IsStatic || method.ReturnType != result)
                    throw new InvalidOperationException(
                        $"{type.Name}.{name}({string.Join(",", Array.ConvertAll(parameters, p => p.Name))}) : {result.Name} was unavailable.");
                return method;
            }
            MethodInfo MH(string contract, Type type, string name, Type result, params Type[] parameters)
            {
                Require(contract, includeContract);
                for (var current = type; current is not null; current = current.BaseType)
                {
                    var method = current.GetMethod(name, Instance | BindingFlags.DeclaredOnly, null, parameters, null);
                    if (method is not null && !method.IsStatic && method.ReturnType == result) return method;
                }
                throw new InvalidOperationException($"{type.Name}.{name} : {result.Name} was unavailable.");
            }

            var tree = T(ContractIds[0], "DiscoveryTreeSO");
            var mode = T(ContractIds[1], "DiscoveryTreeSO+DiscoveryTreeModes");
            var guidContainer = T(ContractIds[2], "GuidContainer");
            var item = T(ContractIds[3], "IDiscoverable");
            var hasGuid = T(ContractIds[4], "IHasGuid");
            var cost = T(ContractIds[5], "ResourceCostList");
            var tuple = T(ContractIds[6], "ResourceTuple");
            var resource = T(ContractIds[7], "ResourceSO");
            var big = resolveType("BigDouble") ?? typeof(BigDouble);

            var all = F(ContractIds[8], tree, "All", typeof(List<>).MakeGenericType(tree), isStatic: true);
            var modeField = F(ContractIds[9], tree, "actionMode", mode);
            var actionTime = F(ContractIds[10], tree, "actionTime", big);
            var rerolls = F(ContractIds[11], tree, "rerollsLeft", typeof(int));
            var used = F(ContractIds[12], tree, "usedRerollsLastDiscover", typeof(bool));
            var currentChoices = F(ContractIds[13], tree, "currentChoiceIds", typeof(List<>).MakeGenericType(guidContainer));
            var nextExclusions = F(ContractIds[14], tree, "nextExcludedIds", typeof(List<>).MakeGenericType(guidContainer));
            var selected = F(ContractIds[15], tree, "selectedChoiceId", guidContainer);
            var total = F(ContractIds[16], tree, "totalDiscoveredCount", typeof(int));
            var pool = F(ContractIds[17], tree, "poolDiscoveredCount", typeof(int));

            var identity = MH(ContractIds[18], tree, "GetGuid", typeof(Guid));
            var guidValue = M(ContractIds[19], guidContainer, "get_guid", typeof(Guid));
            var visible = M(ContractIds[20], tree, "IsVisible", typeof(bool));
            var idle = M(ContractIds[21], tree, "IsInIdleMode", typeof(bool));
            var crafting = M(ContractIds[22], tree, "IsInCraftingMode", typeof(bool));
            var choice = M(ContractIds[23], tree, "IsInChoiceMode", typeof(bool));
            var remaining = M(ContractIds[24], tree, "HasCurrentlyRemMainPoolDiscoveries", typeof(bool));
            var immediate = M(ContractIds[25], tree, "HasImmediateRequiredDiscover", typeof(bool));
            var nextCost = M(ContractIds[26], tree, "GetNextItemCost", cost);
            var resolveItem = M(ContractIds[27], tree, "GetItemFromGuid", item, typeof(Guid));
            var initiate = M(ContractIds[28], tree, "InitiateCraftingMode", typeof(void));
            var select = M(ContractIds[29], tree, "SelectItemId", typeof(void), typeof(Guid));
            var confirm = M(ContractIds[30], tree, "DiscoverSelectedItem", typeof(void));
            var reroll = M(ContractIds[31], tree, "RerollChoices", typeof(void));
            var itemIdentity = M(ContractIds[32], hasGuid, "GetGuid", typeof(Guid));
            var discovered = M(ContractIds[33], item, "IsDiscovered", typeof(bool));
            var required = M(ContractIds[34], item, "IsDiscoverRequired", typeof(bool));
            var entries = M(ContractIds[35], cost, "GetEntries", typeof(List<>).MakeGenericType(tuple));
            var enough = M(ContractIds[36], cost, "HasEnough", typeof(bool));
            var perform = M(ContractIds[37], cost, "PerformCost", typeof(void));
            var tupleResource = F(ContractIds[38], tuple, "resource", resource);
            var tupleValue = M(ContractIds[39], tuple, "GetValue", big);
            var resourceIdentity = MH(ContractIds[40], resource, "GetGuid", typeof(Guid));
            var resourceQuantity = M(ContractIds[41], resource, "GetTrueQuantity", big);
            var maxRerolls = M(ContractIds[42], tree, "GetMaxRerolls", typeof(int));

            bindings = new DiscoveryTreeOfferNativeBindings(
                tree, item, cost, tuple, resource,
                StaticListGetter(all),
                InstanceFunc<Guid>(identity),
                EnumFieldGetter(modeField),
                FieldGetter<BigDouble>(actionTime),
                FieldGetter<int>(rerolls),
                FieldGetter<bool>(used),
                ListFieldGetter(currentChoices),
                ListFieldGetter(nextExclusions),
                ObjectFieldGetter(selected),
                InstanceFunc<Guid>(guidValue),
                FieldGetter<int>(total),
                FieldGetter<int>(pool),
                InstanceFunc<bool>(visible),
                InstanceFunc<bool>(idle),
                InstanceFunc<bool>(crafting),
                InstanceFunc<bool>(choice),
                InstanceFunc<bool>(remaining),
                InstanceFunc<bool>(immediate),
                InstanceFunc<int>(maxRerolls),
                InstanceObjectFunc(nextCost),
                InstanceObjectFunc<Guid>(resolveItem),
                InstanceAction(initiate),
                InstanceAction<Guid>(select),
                InstanceAction(confirm),
                InstanceAction(reroll),
                InstanceFunc<Guid>(itemIdentity),
                InstanceFunc<bool>(discovered),
                InstanceFunc<bool>(required),
                InstanceListFunc(entries),
                InstanceFunc<bool>(enough),
                InstanceAction(perform),
                ObjectFieldGetter(tupleResource),
                InstanceFunc<BigDouble>(tupleValue),
                InstanceFunc<Guid>(resourceIdentity),
                InstanceFunc<BigDouble>(resourceQuantity));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete Discovery Tree offer binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private static Func<IList> StaticListGetter(FieldInfo field)
    {
        var body = Expression.Convert(Expression.Field(null, field), typeof(IList));
        return Expression.Lambda<Func<IList>>(body).Compile();
    }

    private static Func<object, T> FieldGetter<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(T));
        return Expression.Lambda<Func<object, T>>(body, target).Compile();
    }

    private static Func<object, int> EnumFieldGetter(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Field(Expression.Convert(target, field.DeclaringType!), field);
        return Expression.Lambda<Func<object, int>>(Expression.Convert(value, typeof(int)), target).Compile();
    }

    private static Func<object, IList> ListFieldGetter(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(IList));
        return Expression.Lambda<Func<object, IList>>(body, target).Compile();
    }

    private static Func<object, object> ObjectFieldGetter(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object));
        return Expression.Lambda<Func<object, object>>(body, target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, T>>(Expression.Convert(call, typeof(T)), target).Compile();
    }

    private static Func<object, object> InstanceObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), target).Compile();
    }

    private static Func<object, TArg, object?> InstanceObjectFunc<TArg>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(TArg), "argument");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method, argument);
        return Expression.Lambda<Func<object, TArg, object?>>(Expression.Convert(call, typeof(object)), target, argument).Compile();
    }

    private static Func<object, IList> InstanceListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, IList>>(Expression.Convert(call, typeof(IList)), target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, TArg> InstanceAction<TArg>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(TArg), "argument");
        return Expression.Lambda<Action<object, TArg>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, argument), target, argument).Compile();
    }
}
