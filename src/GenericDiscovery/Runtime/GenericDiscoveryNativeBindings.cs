using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for the generic <c>IDiscoverable</c> transaction. Reflection is
/// confined to construction; execution uses only exact compiled delegates and the shared typed
/// identity registry.
/// </summary>
internal sealed class GenericDiscoveryNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "generic-discovery.discoverable.type-action",
        "generic-discovery.cost-list.type-action",
        "generic-discovery.cost-entry.type-action",
        "generic-discovery.resource.type-action",
        "generic-discovery.alchemy-recipe.type-action",
        "generic-discovery.equipment.type-action",
        "generic-discovery.glyph.type-action",
        "generic-discovery.ritual.type-action",
        "generic-discovery.time-rune.type-action",
        "generic-discovery.get-cost-action",
        "generic-discovery.is-visible-action",
        "generic-discovery.can-discover-action",
        "generic-discovery.is-discovered-action",
        "generic-discovery.is-required-action",
        "generic-discovery.discover-action",
        "generic-discovery.cost-entries-action",
        "generic-discovery.cost-enough-action",
        "generic-discovery.cost-perform-action",
        "generic-discovery.entry-resource-action",
        "generic-discovery.entry-value-action",
        "generic-discovery.resource-identity-action",
        "generic-discovery.resource-amount-action",
        "generic-discovery.get-glyph-recipe-action",
        "generic-discovery.get-resource-recipe-action",
    };

    private GenericDiscoveryNativeBindings(
        Type discoverableType,
        Type costType,
        Type resourceType,
        IReadOnlyDictionary<string, Type> supportedTypes,
        Func<object, object> getCost,
        Func<object, bool> isVisible,
        Func<object, bool> canDiscover,
        Func<object, bool> isDiscovered,
        Func<object, bool> isRequired,
        Action<object> discover,
        Func<object, IList> getEntries,
        Func<object, bool> hasEnough,
        Action<object> performCost,
        Func<object, object> readResource,
        Func<object, BigDouble> readCost,
        Func<object, Guid> readResourceIdentity,
        Func<object, BigDouble> readResourceAmount,
        Func<object, IList> getGlyphRecipe,
        Func<object, IList> getResourceRecipe)
    {
        DiscoverableType = discoverableType;
        CostType = costType;
        ResourceType = resourceType;
        SupportedTypes = supportedTypes;
        GetCost = getCost;
        IsVisible = isVisible;
        CanDiscover = canDiscover;
        IsDiscovered = isDiscovered;
        IsRequired = isRequired;
        Discover = discover;
        GetEntries = getEntries;
        HasEnough = hasEnough;
        PerformCost = performCost;
        ReadResource = readResource;
        ReadCost = readCost;
        ReadResourceIdentity = readResourceIdentity;
        ReadResourceAmount = readResourceAmount;
        GetGlyphRecipe = getGlyphRecipe;
        GetResourceRecipe = getResourceRecipe;
    }

    internal Type DiscoverableType { get; }
    internal Type CostType { get; }
    internal Type GlyphType => SupportedTypes["GlyphSO"];
    internal Type ResourceType { get; }
    internal IReadOnlyDictionary<string, Type> SupportedTypes { get; }
    internal Func<object, object> GetCost { get; }
    internal Func<object, bool> IsVisible { get; }
    internal Func<object, bool> CanDiscover { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> IsRequired { get; }
    internal Action<object> Discover { get; }
    internal Func<object, IList> GetEntries { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Action<object> PerformCost { get; }
    internal Func<object, object> ReadResource { get; }
    internal Func<object, BigDouble> ReadCost { get; }
    internal Func<object, Guid> ReadResourceIdentity { get; }
    internal Func<object, BigDouble> ReadResourceAmount { get; }
    internal Func<object, IList> GetGlyphRecipe { get; }
    internal Func<object, IList> GetResourceRecipe { get; }

    internal static bool TryCreate(
        out GenericDiscoveryNativeBindings? bindings,
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
                return resolveType(name) ??
                    throw new InvalidOperationException(name + " was unavailable");
            }
            MethodInfo M(
                string contract,
                Type type,
                string name,
                Type result,
                params Type[] parameters)
            {
                Require(contract, includeContract);
                var method = type.GetMethod(name, Instance, null, parameters, null);
                if (method is null || method.IsStatic || method.ReturnType != result)
                    throw new InvalidOperationException(
                        type.Name + "." + name + " did not match the audited signature");
                return method;
            }
            MethodInfo MH(string contract, Type type, string name, Type result)
            {
                Require(contract, includeContract);
                for (var current = type; current is not null; current = current.BaseType)
                {
                    var method = current.GetMethod(
                        name,
                        Instance | BindingFlags.DeclaredOnly,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (method is not null && !method.IsStatic && method.ReturnType == result)
                        return method;
                }
                throw new InvalidOperationException(
                    type.Name + "." + name + " did not match the audited inherited signature");
            }
            FieldInfo F(string contract, Type type, string name, Type fieldType)
            {
                Require(contract, includeContract);
                var field = type.GetField(name, Instance);
                if (field is null || field.IsStatic || field.FieldType != fieldType)
                    throw new InvalidOperationException(
                        type.Name + "." + name + " did not match the audited field signature");
                return field;
            }

            var discoverable = T(ContractIds[0], "IDiscoverable");
            var cost = T(ContractIds[1], "ResourceCostList");
            var entry = T(ContractIds[2], "ResourceTuple");
            var resource = T(ContractIds[3], "ResourceSO");
            var supported = new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["AlchemyRecipeSO"] = T(ContractIds[4], "AlchemyRecipeSO"),
                ["EquipmentSO"] = T(ContractIds[5], "EquipmentSO"),
                ["GlyphSO"] = T(ContractIds[6], "GlyphSO"),
                ["RitualSO"] = T(ContractIds[7], "RitualSO"),
                ["TimeRuneSO"] = T(ContractIds[8], "TimeRuneSO"),
            };
            foreach (var pair in supported)
                if (!discoverable.IsAssignableFrom(pair.Value))
                    throw new InvalidOperationException(
                        pair.Key + " does not implement the exact IDiscoverable contract");

            var big = resolveType("BigDouble") ?? typeof(BigDouble);
            var getCost = M(ContractIds[9], discoverable, "GetDiscoverCost", cost);
            var visible = M(ContractIds[10], discoverable, "IsDiscoverVisible", typeof(bool));
            var canDiscover = M(ContractIds[11], discoverable, "CanDiscover", typeof(bool));
            var discovered = M(ContractIds[12], discoverable, "IsDiscovered", typeof(bool));
            var required = M(ContractIds[13], discoverable, "IsDiscoverRequired", typeof(bool));
            var discover = M(ContractIds[14], discoverable, "Discover", typeof(void));
            var entries = M(
                ContractIds[15],
                cost,
                "GetEntries",
                typeof(List<>).MakeGenericType(entry));
            var enough = M(ContractIds[16], cost, "HasEnough", typeof(bool));
            var perform = M(ContractIds[17], cost, "PerformCost", typeof(void));
            var entryResource = F(ContractIds[18], entry, "resource", resource);
            var entryValue = M(ContractIds[19], entry, "GetValue", big);
            var resourceIdentity = MH(ContractIds[20], resource, "GetGuid", typeof(Guid));
            var resourceAmount = M(ContractIds[21], resource, "GetTrueQuantity", big);
            var glyphRecipe = M(
                ContractIds[22],
                discoverable,
                "GetGlyphRecipe",
                typeof(List<>).MakeGenericType(supported["GlyphSO"]));
            var resourceRecipe = M(
                ContractIds[23],
                discoverable,
                "GetResourceRecipe",
                typeof(List<>).MakeGenericType(resource));

            bindings = new GenericDiscoveryNativeBindings(
                discoverable,
                cost,
                resource,
                supported,
                InstanceObjectFunc(getCost),
                InstanceFunc<bool>(visible),
                InstanceFunc<bool>(canDiscover),
                InstanceFunc<bool>(discovered),
                InstanceFunc<bool>(required),
                InstanceAction(discover),
                InstanceListFunc(entries),
                InstanceFunc<bool>(enough),
                InstanceAction(perform),
                ObjectFieldGetter(entryResource),
                InstanceFunc<BigDouble>(entryValue),
                InstanceFunc<Guid>(resourceIdentity),
                InstanceFunc<BigDouble>(resourceAmount),
                InstanceListFunc(glyphRecipe),
                InstanceListFunc(resourceRecipe));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete generic discovery binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string contract, Func<string, bool> include)
    {
        if (!include(contract))
            throw new InvalidOperationException("Required contract " + contract + " was withheld");
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(call, typeof(T)), target).Compile();
    }

    private static Func<object, object> InstanceObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(call, typeof(object)), target).Compile();
    }

    private static Func<object, IList> InstanceListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var call = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(call, typeof(IList)), target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Func<object, object> ObjectFieldGetter(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Field(Expression.Convert(target, field.DeclaringType!), field);
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(value, typeof(object)), target).Compile();
    }
}
