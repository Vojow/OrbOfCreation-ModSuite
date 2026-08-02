using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native equipment loadout click pipeline.</summary>
internal sealed class EquipmentLoadoutNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "equipment-loadout.equipment.type-action", "equipment-loadout.manager.type-action",
        "equipment-loadout.list.type-action", "equipment-loadout.equipment-type.type-action",
        "equipment-loadout.cost.type-action", "equipment-loadout.int-variable.type-action",
        "equipment-loadout.global-variables.type-action", "equipment-loadout.manager-instance-action",
        "equipment-loadout.manager-equipped-list-action", "equipment-loadout.equipment-created-action",
        "equipment-loadout.equipment-type-field-action", "equipment-loadout.equipment-maximum-action",
        "equipment-loadout.equipment-cost-action", "equipment-loadout.list-stacks-action",
        "equipment-loadout.list-maximum-action", "equipment-loadout.list-at-maximum-action",
        "equipment-loadout.list-values-action", "equipment-loadout.list-type-count-action",
        "equipment-loadout.type-maximum-action", "equipment-loadout.cost-enough-action",
        "equipment-loadout.cost-maximum-times-action", "equipment-loadout.global-multi-buy-action",
        "equipment-loadout.int-as-int-action", "equipment-loadout.manager-equip-action",
        "equipment-loadout.manager-unequip-action",
    };

    private EquipmentLoadoutNativeBindings(Type equipmentType, Type managerType,
        Func<object?> manager, Func<object, object?> equippedList, Func<object, bool> isCreated,
        Func<object, object?> readEquipmentType, Func<object, int> maximumStacks,
        Func<object, object?> usageCost, Func<object, object, int> stacks,
        Func<object, int> maximumSlots, Func<object, bool> atMaximum, Func<object, IList?> values,
        Func<object, object, int> typeCount, Func<object, int> typeMaximum,
        Func<object, bool> hasEnough, Func<object, BigDouble> maximumTimes,
        Func<object?> multiBuy, Func<object, int> asInt,
        Action<object, object> equip, Action<object, object> unequip)
    {
        EquipmentType = equipmentType; ManagerType = managerType; Manager = manager;
        EquippedList = equippedList; IsCreated = isCreated; ReadEquipmentType = readEquipmentType;
        MaximumStacks = maximumStacks; UsageCost = usageCost; Stacks = stacks;
        MaximumSlots = maximumSlots; AtMaximum = atMaximum; Values = values;
        TypeCount = typeCount; TypeMaximum = typeMaximum; HasEnough = hasEnough;
        MaximumTimes = maximumTimes; MultiBuy = multiBuy; AsInt = asInt;
        Equip = equip; Unequip = unequip;
    }

    internal Type EquipmentType { get; }
    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> EquippedList { get; }
    internal Func<object, bool> IsCreated { get; }
    internal Func<object, object?> ReadEquipmentType { get; }
    internal Func<object, int> MaximumStacks { get; }
    internal Func<object, object?> UsageCost { get; }
    internal Func<object, object, int> Stacks { get; }
    internal Func<object, int> MaximumSlots { get; }
    internal Func<object, bool> AtMaximum { get; }
    internal Func<object, IList?> Values { get; }
    internal Func<object, object, int> TypeCount { get; }
    internal Func<object, int> TypeMaximum { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<object, BigDouble> MaximumTimes { get; }
    internal Func<object?> MultiBuy { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object, object> Equip { get; }
    internal Action<object, object> Unequip { get; }

    internal static bool TryCreate(out EquipmentLoadoutNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(ContractIds[index], includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }
            var equipment = T(0, "EquipmentSO");
            var managerType = T(1, "EquipmentManager");
            var list = T(2, "EquipmentListVariable");
            var equipmentKind = T(3, "EquipmentTypeSO");
            var cost = T(4, "ResourceCostList");
            var integer = T(5, "IntVariable");
            var globals = T(6, "GlobalVariables");
            var big = resolveType("BigDouble") ?? typeof(BigDouble);
            var manager = StaticField(7, managerType, "instance", managerType, includeContract);
            var equipped = Field(8, managerType, "equippedEquipment", list, includeContract);
            var created = Method(9, equipment, "IsCreated", typeof(bool), includeContract);
            var kind = Field(10, equipment, "equipmentType", equipmentKind, includeContract);
            var maxStacks = Method(11, equipment, "GetMaxLevel", typeof(int), includeContract);
            var usage = Method(12, equipment, "GetUsageCost", cost, includeContract);
            var getStacks = Method(13, list, "GetStacks", typeof(int), includeContract, equipment);
            var maxSlots = Method(14, list, "GetMax", typeof(int), includeContract);
            var atMax = Method(15, list, "IsAtMax", typeof(bool), includeContract);
            var values = Field(16, list, "value", typeof(System.Collections.Generic.List<>).MakeGenericType(equipment), includeContract);
            var typeCount = Method(17, list, "GetTypesEquipped", typeof(int), includeContract, equipmentKind);
            var typeMax = Method(18, equipmentKind, "GetMaxTypeSlots", typeof(int), includeContract);
            var enough = Method(19, cost, "HasEnough", typeof(bool), includeContract);
            var maximumTimes = Method(20, cost, "MaximumCostTimes", big, includeContract);
            var getMultiBuy = StaticMethod(21, globals, "GetMultiBuy", integer, includeContract);
            var asInt = Method(22, integer, "AsInt", typeof(int), includeContract);
            var equip = Method(23, managerType, "EquipItem", typeof(void), includeContract, equipment);
            var unequip = Method(24, managerType, "UnEquipItem", typeof(void), includeContract, equipment);

            bindings = new EquipmentLoadoutNativeBindings(equipment, managerType,
                StaticObject(manager), ObjectField(equipped), Func<bool>(created), ObjectField(kind),
                Func<int>(maxStacks), ObjectFunc(usage), Func2<int>(getStacks), Func<int>(maxSlots),
                Func<bool>(atMax), ListField(values), Func2<int>(typeCount), Func<int>(typeMax),
                Func<bool>(enough), Func<BigDouble>(maximumTimes), StaticObject(getMultiBuy),
                Func<int>(asInt), Action2(equip), Action2(unequip));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or AmbiguousMatchException or ArgumentException)
        {
            reason = "The complete equipment loadout binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    { if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld"); }

    private static MethodInfo Method(int index, Type owner, string name, Type result,
        Func<string, bool> include, params Type[] parameters)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static MethodInfo StaticMethod(int index, Type owner, string name, Type result, Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var method = owner.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited static signature");
        return method;
    }

    private static FieldInfo Field(int index, Type owner, string name, Type type, Func<string, bool> include)
    {
        Require(ContractIds[index], include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static FieldInfo StaticField(int index, Type owner, string name, Type type, Func<string, bool> include)
    {
        Require(ContractIds[index], include);
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
                Expression.Convert(argument, method.GetParameters()[0].ParameterType)), typeof(T)), target, argument).Compile();
    }

    private static Action<object, object> Action2(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var argument = Expression.Parameter(typeof(object), "argument");
        return Expression.Lambda<Action<object, object>>(Expression.Call(
            Expression.Convert(target, method.DeclaringType!), method,
            Expression.Convert(argument, method.GetParameters()[0].ParameterType)), target, argument).Compile();
    }

    private static Func<object?> StaticObject(MemberInfo member)
    {
        Expression value = member is FieldInfo field ? Expression.Field(null, field) : Expression.Call((MethodInfo)member);
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
