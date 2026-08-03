using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native ordinary-alchemy list UI pipeline.</summary>
internal sealed class AlchemyLoadoutNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "alchemy-loadout.recipe.type-action", "alchemy-loadout.manager.type-action",
        "alchemy-loadout.list.type-action", "alchemy-loadout.instance.type-action",
        "alchemy-loadout.cost.type-action", "alchemy-loadout.int-variable.type-action",
        "alchemy-loadout.global-variables.type-action", "alchemy-loadout.manager-instance-action",
        "alchemy-loadout.manager-active-action", "alchemy-loadout.recipe-discovered-action",
        "alchemy-loadout.recipe-usage-cost-action", "alchemy-loadout.recipe-free-uses-action",
        "alchemy-loadout.recipe-maximum-uses-action", "alchemy-loadout.list-can-add-action",
        "alchemy-loadout.list-values-action", "alchemy-loadout.instance-reference-action",
        "alchemy-loadout.instance-queued-action", "alchemy-loadout.instance-remaining-free-action",
        "alchemy-loadout.instance-remaining-maximum-action", "alchemy-loadout.cost-maximum-times-action",
        "alchemy-loadout.cost-empty-action", "alchemy-loadout.global-multi-buy-action",
        "alchemy-loadout.int-as-int-action", "alchemy-loadout.list-engage-action",
        "alchemy-loadout.list-disengage-action", "alchemy-loadout.list-swap-action",
        "alchemy-loadout.list-update-action",
    };

    private AlchemyLoadoutNativeBindings(Type recipeType, Type managerType,
        Func<object?> manager, Func<object, object?> activeList, Func<object, bool> discovered,
        Func<object, object?> usageCost, Func<object, int> freeUses, Func<object, int> maximumUses,
        Func<object, object, bool> canAdd, Func<object, IList?> values,
        Func<object, object?> instanceRecipe, Func<object, int> queued,
        Func<object, int> remainingFree, Func<object, int> remainingMaximum,
        Func<object, BigDouble> maximumTimes, Func<object, bool> costEmpty,
        Func<object?> multiBuy, Func<object, int> asInt, Action<object, object> engage,
        Action<object, object> disengage, Action<object, int, int> swap, Action<object> update)
    {
        RecipeType = recipeType; ManagerType = managerType; Manager = manager;
        ActiveList = activeList; Discovered = discovered; UsageCost = usageCost;
        FreeUses = freeUses; MaximumUses = maximumUses; CanAdd = canAdd; Values = values;
        InstanceRecipe = instanceRecipe; Queued = queued; RemainingFree = remainingFree;
        RemainingMaximum = remainingMaximum; MaximumTimes = maximumTimes; CostEmpty = costEmpty;
        MultiBuy = multiBuy; AsInt = asInt; Engage = engage; Disengage = disengage;
        Swap = swap; Update = update;
    }

    internal Type RecipeType { get; }
    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> ActiveList { get; }
    internal Func<object, bool> Discovered { get; }
    internal Func<object, object?> UsageCost { get; }
    internal Func<object, int> FreeUses { get; }
    internal Func<object, int> MaximumUses { get; }
    internal Func<object, object, bool> CanAdd { get; }
    internal Func<object, IList?> Values { get; }
    internal Func<object, object?> InstanceRecipe { get; }
    internal Func<object, int> Queued { get; }
    internal Func<object, int> RemainingFree { get; }
    internal Func<object, int> RemainingMaximum { get; }
    internal Func<object, BigDouble> MaximumTimes { get; }
    internal Func<object, bool> CostEmpty { get; }
    internal Func<object?> MultiBuy { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object, object> Engage { get; }
    internal Action<object, object> Disengage { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> Update { get; }

    internal static bool TryCreate(out AlchemyLoadoutNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(index, includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }
            var recipe = T(0, "AlchemyRecipeSO");
            var managerType = T(1, "AlchemyManager");
            var list = T(2, "AlchemyInstanceListVariable");
            var instance = T(3, "AlchemyInstance");
            var cost = T(4, "ResourceCostList");
            var integer = T(5, "IntVariable");
            var globals = T(6, "GlobalVariables");
            var big = resolveType("BigDouble") ?? typeof(BigDouble);
            var manager = StaticField(7, managerType, "instance", managerType, includeContract);
            var active = Field(8, managerType, "activeAlchemy", list, includeContract);
            var discovered = Method(9, recipe, "IsDiscovered", typeof(bool), includeContract);
            var usage = Method(10, recipe, "GetUsageCost", cost, includeContract);
            var free = Method(11, recipe, "GetFreeUsageSlots", typeof(int), includeContract);
            var maximum = Method(12, recipe, "GetMaxUsageSlots", typeof(int), includeContract);
            var canAdd = Method(13, list, "CanAddInstance", typeof(bool), includeContract, recipe);
            var values = Field(14, list, "value",
                typeof(System.Collections.Generic.List<>).MakeGenericType(instance), includeContract);
            var reference = Method(15, instance, "get_reference", recipe, includeContract);
            var queued = Method(16, instance, "GetQueuedQuantity", typeof(int), includeContract);
            var remainingFree = Method(17, instance, "GetRemainingFreeUsageSlots", typeof(int), includeContract);
            var remainingMax = Method(18, instance, "GetRemainingMaxUsageSlots", typeof(int), includeContract);
            var maximumTimes = Method(19, cost, "MaximumCostTimes", big, includeContract);
            var empty = Method(20, cost, "IsEmpty", typeof(bool), includeContract);
            var getMultiBuy = StaticMethod(21, globals, "GetMultiBuy", integer, includeContract);
            var asInt = Method(22, integer, "AsInt", typeof(int), includeContract);
            var engage = Method(23, list, "EngageAlchemy", typeof(void), includeContract, recipe);
            var disengage = Method(24, list, "DisengageAlchemy", typeof(void), includeContract, recipe);
            var swap = Method(25, list, "SwapPositions", typeof(void), includeContract,
                typeof(int), typeof(int));
            var update = Method(26, list, "UpdateObservable", typeof(void), includeContract);

            bindings = new AlchemyLoadoutNativeBindings(recipe, managerType,
                StaticObject(manager), ObjectField(active), Func<bool>(discovered),
                ObjectFunc(usage), Func<int>(free), Func<int>(maximum), Func2<bool>(canAdd),
                ListField(values), ObjectFunc(reference), Func<int>(queued), Func<int>(remainingFree),
                Func<int>(remainingMax), Func<BigDouble>(maximumTimes), Func<bool>(empty),
                StaticObject(getMultiBuy), Func<int>(asInt), Action2(engage), Action2(disengage),
                Action3(swap), Action1(update));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete ordinary alchemy binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        var id = ContractIds[index];
        if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld");
    }

    private static MethodInfo Method(int index, Type owner, string name, Type result,
        Func<string, bool> include, params Type[] parameters)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static MethodInfo StaticMethod(int index, Type owner, string name, Type result,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited static signature");
        return method;
    }

    private static FieldInfo Field(int index, Type owner, string name, Type type,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static FieldInfo StaticField(int index, Type owner, string name, Type type,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited static field");
        return field;
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(Expression.Convert(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(T)), target).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Func<object, object, T> Func2<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Func<object, object, T>>(Expression.Convert(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(argument, method.GetParameters()[0].ParameterType)), typeof(T)),
            target, argument).Compile();
    }

    private static Action<object, object> Action2(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Action<object, object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(argument, method.GetParameters()[0].ParameterType)), target, argument).Compile();
    }

    private static Action<object, int, int> Action3(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(int), "first");
        var second = Expression.Parameter(typeof(int), "second");
        return Expression.Lambda<Action<object, int, int>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method, first, second),
            target, first, second).Compile();
    }

    private static Action<object> Action1(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object?> StaticObject(MemberInfo member)
    {
        Expression value = member is FieldInfo field ? Expression.Field(null, field) :
            Expression.Call((MethodInfo)member);
        return Expression.Lambda<Func<object?>>(Expression.Convert(value, typeof(object))).Compile();
    }

    private static Func<object, object?> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

    private static Func<object, IList?> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(IList)), target).Compile();
    }
}
