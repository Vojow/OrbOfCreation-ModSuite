using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoHarvestReflectionTypes
{
    private AutoHarvestReflectionTypes()
    {
    }

    public Type Plot { get; private set; } = null!;
    public Type Action { get; private set; } = null!;
    public Type Instance { get; private set; } = null!;
    public Type ActiveActions { get; private set; } = null!;
    public Type ScalingWeight { get; private set; } = null!;
    public Type RewardPool { get; private set; } = null!;

    public static AutoHarvestReflectionTypes Discover() => new()
    {
        Plot = RequireLoadedType(KnownEntities.FruitTreePlot.ManagedTypeName),
        Action = RequireLoadedType(KnownEntities.FruitTreeCollect.ManagedTypeName),
        Instance = RequireLoadedType("PlotNodeActionInstance"),
        ActiveActions = RequireLoadedType(KnownEntities.ActivePlotNodeActions.ManagedTypeName),
        ScalingWeight = RequireLoadedType(KnownEntities.CompletionScalingWeight.ManagedTypeName),
        RewardPool = RequireLoadedType(KnownEntities.FruitTreeRewardPool.ManagedTypeName),
    };

    internal static Type RequireLoadedType(string name) =>
        ReflectionUtil.FindLoadedType(name) ??
        throw new AutoHarvestRegistryNotReadyException($"native type {name} is not registered yet");
}

internal sealed class AutoHarvestReflectionContract
{
    private AutoHarvestReflectionContract(AutoHarvestReflectionTypes types)
    {
        Types = types;
    }

    public AutoHarvestReflectionTypes Types { get; }
    public AutoHarvestStableIdAccessor PlotStableId { get; private set; } = null!;
    public AutoHarvestStableIdAccessor ActionStableId { get; private set; } = null!;

    public Func<object, object?> ActionPrerequisites { get; private set; } = null!;
    public Func<object, bool> PrerequisitesAvailable { get; private set; } = null!;
    public Func<object, bool> PrerequisitesCheck { get; private set; } = null!;

    public MethodInfo PlotGetActionInstances { get; private set; } = null!;

    public MethodInfo InstanceGetAction { get; private set; } = null!;
    public MethodInfo InstanceGetElement { get; private set; } = null!;
    public MethodInfo InstanceIsEmpty { get; private set; } = null!;
    public MethodInfo InstanceIsEngaged { get; private set; } = null!;
    public MethodInfo InstanceGetActualQuantity { get; private set; } = null!;

    public FieldInfo ActiveValues { get; private set; } = null!;
    public MethodInfo ActiveGetUsedSpots { get; private set; } = null!;
    public MethodInfo ActiveHasEmptySpot { get; private set; } = null!;
    public MethodInfo ActiveAddInstance { get; private set; } = null!;

    public static AutoHarvestReflectionContract Bind(AutoHarvestReflectionTypes types)
    {
        var contract = new AutoHarvestReflectionContract(types);
        contract.PlotStableId = AutoHarvestStableIdAccessor.Bind(types.Plot);
        contract.ActionStableId = AutoHarvestStableIdAccessor.Bind(types.Action);
        var prerequisitesField = RequireField(types.Action, "prerequisites");
        contract.ActionPrerequisites =
            NativeAccessorBinder.Reference(types.Action, prerequisitesField.Name) ??
            throw new InvalidOperationException(
                $"{types.Action.FullName}.prerequisites reference contract is unavailable");
        contract.PrerequisitesAvailable =
            NativeAccessorBinder.Field<bool>(prerequisitesField.FieldType, "available") ??
            throw new InvalidOperationException(
                $"{prerequisitesField.FieldType.FullName}.available Boolean contract is unavailable");
        contract.PrerequisitesCheck =
            NativeAccessorBinder.Call<bool>(prerequisitesField.FieldType, "Check") ??
            throw new InvalidOperationException(
                $"{prerequisitesField.FieldType.FullName}.Check() Boolean contract is unavailable");
        contract.PlotGetActionInstances = RequireListMethod(types.Plot, "GetActionInstances", types.Instance);

        contract.InstanceGetAction = RequireMethod(types.Instance, "GetAction", types.Action);
        contract.InstanceGetElement = RequireMethod(types.Instance, "GetElement", types.Plot);
        contract.InstanceIsEmpty = RequireMethod(types.Instance, "IsEmpty", typeof(bool));
        contract.InstanceIsEngaged = RequireMethod(types.Instance, "IsEngaged", typeof(bool));
        contract.InstanceGetActualQuantity = RequireMethod(types.Instance, "GetActualQuantity", typeof(int));

        contract.ActiveValues = RequireListField(types.ActiveActions, "value", types.Instance);
        contract.ActiveGetUsedSpots = RequireMethod(types.ActiveActions, "GetUsedSpots", typeof(int));
        contract.ActiveHasEmptySpot = RequireMethod(types.ActiveActions, "HasEmptySpot", typeof(bool));
        contract.ActiveAddInstance = RequireMethod(types.ActiveActions, "AddInstance", typeof(void), types.Instance, typeof(int));
        return contract;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        throw new InvalidOperationException($"{type.FullName}.{name} field is unavailable");
    }

    private static FieldInfo RequireListField(Type type, string name, Type? expectedElementType = null)
    {
        var field = RequireField(type, name);
        if (!typeof(IList).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException($"{type.FullName}.{name} field type is not a list");
        if (expectedElementType is not null && ElementType(field.FieldType) != expectedElementType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} list element type is not {expectedElementType.FullName}");
        return field;
    }

    private static MethodInfo RequireMethod(Type type, string name, Type? returnType, params Type[] parameters)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameters,
            modifiers: null);
        if (method is null || returnType is not null && method.ReturnType != returnType)
            throw new InvalidOperationException($"{type.FullName}.{name} method contract is unavailable");
        return method;
    }

    private static MethodInfo RequireListMethod(Type type, string name, Type expectedElementType)
    {
        var method = RequireMethod(type, name, null);
        if (!typeof(IList).IsAssignableFrom(method.ReturnType) || ElementType(method.ReturnType) != expectedElementType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} return type is not a list of {expectedElementType.FullName}");
        return method;
    }

    private static Type ElementType(Type collectionType) =>
        collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetGenericArguments().Length == 1
                ? collectionType.GetGenericArguments()[0]
                : throw new InvalidOperationException($"{collectionType.FullName} has no single element type");
}
