using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for equipped-spell removal and reordering. Reflection is confined
/// to construction; execution uses exact compiled delegates only.
/// </summary>
internal sealed class SpellLoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-manager.instance",
        "spell-manager.active-spells",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.list-value-action",
        "spell-workbench.spell-guid-container-action",
        "discovery-tree-offer.guid-container-value",
        "spell-loadout.spell-is-empty-action",
        "spell-loadout.spell-can-remove-action",
        "spell-loadout.spell-get-name-action",
        "spell-loadout.manager-remove-spell-action",
        "spell-loadout.list-swap-positions-action",
        "spell-loadout.list-update-observable-action",
    };

    private SpellLoadoutNativeBindings(
        Type spellType,
        Func<object?> manager,
        Func<object, object> active,
        Func<object, IList> activeValues,
        Func<object, object?> spellGuid,
        Func<object, Guid> guidValue,
        Func<object, bool> isEmpty,
        Func<object, bool> canRemove,
        Func<object, string> getName,
        Action<object, object> remove,
        Action<object, int, int> swap,
        Action<object> updateObservable)
    {
        SpellType = spellType;
        ReadManager = manager;
        ReadActive = active;
        ReadActiveValues = activeValues;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        IsEmpty = isEmpty;
        CanRemove = canRemove;
        GetName = getName;
        Remove = remove;
        Swap = swap;
        UpdateObservable = updateObservable;
    }

    internal Type SpellType { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }
    internal Func<object, bool> IsEmpty { get; }
    internal Func<object, bool> CanRemove { get; }
    internal Func<object, string> GetName { get; }
    internal Action<object, object> Remove { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> UpdateObservable { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellLoadoutNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            for (var index = 0; index < ContractIds.Length; index++)
                Require(ContractIds[index], includeContract);

            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable.");

            var managerType = T("SpellManager");
            var spellListType = T("SpellListVariable");
            var spellType = T("Spell");
            var guidType = T("GuidContainer");
            var listType = typeof(List<>).MakeGenericType(spellType);

            var managerInstance = Field(managerType, "instance", managerType, isStatic: true);
            var active = Field(managerType, "activeSpells", spellListType, isStatic: false);
            var values = HierarchyField(spellListType, "value", listType);
            var spellGuid = Field(spellType, "guidContainer", guidType, isStatic: false);
            var guidValue = Method(guidType, "get_guid", typeof(Guid));
            var isEmpty = Method(spellType, "IsEmpty", typeof(bool));
            var canRemove = Method(spellType, "CanRemove", typeof(bool));
            var getName = Method(spellType, "GetName", typeof(string));
            var remove = Method(managerType, "RemoveSpell", typeof(void), spellType);
            var swap = HierarchyMethod(
                spellListType,
                "SwapPositions",
                typeof(void),
                typeof(int),
                typeof(int));
            var update = HierarchyMethod(spellListType, "UpdateObservable", typeof(void));

            bindings = new SpellLoadoutNativeBindings(
                spellType,
                StaticObject(managerInstance),
                ObjectField(active),
                ListField(values),
                NullableObjectField(spellGuid),
                InstanceFunc<Guid>(guidValue),
                InstanceFunc<bool>(isEmpty),
                InstanceFunc<bool>(canRemove),
                InstanceFunc<string>(getName),
                InstanceObjectAction(remove),
                InstanceValueValueAction<int, int>(swap),
                InstanceAction(update));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete spell loadout binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id))
            throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic)
    {
        var field = type.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static FieldInfo HierarchyField(Type type, string name, Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null && field.FieldType == valueType) return field;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo Method(
        Type type,
        string name,
        Type result,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(
        Type type,
        string name,
        Type result,
        params Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method is not null && !method.IsStatic && method.ReturnType == result)
                return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(object));
        return Expression.Lambda<Func<object, object>>(body, target).Compile();
    }

    private static Func<object, object?> NullableObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, target).Compile();
    }

    private static Func<object, IList> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(IList));
        return Expression.Lambda<Func<object, IList>>(body, target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, T>>(body, target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Action<object, object> InstanceObjectAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
            target,
            value).Compile();
    }

    private static Action<object, TFirst, TSecond> InstanceValueValueAction<TFirst, TSecond>(
        MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(TFirst), "first");
        var second = Expression.Parameter(typeof(TSecond), "second");
        return Expression.Lambda<Action<object, TFirst, TSecond>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                first,
                second),
            target,
            first,
            second).Compile();
    }
}
