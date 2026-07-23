using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

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
    public Type ScalingWeightEffectMod { get; private set; } = null!;
    public Type TreasurePoolEffect { get; private set; } = null!;

    public static AutoHarvestReflectionTypes Discover() => new()
    {
        Plot = RequireLoadedType(KnownEntities.FruitTreePlot.ManagedTypeName),
        Action = RequireLoadedType(KnownEntities.FruitTreeCollect.ManagedTypeName),
        Instance = RequireLoadedType("PlotNodeActionInstance"),
        ActiveActions = RequireLoadedType(KnownEntities.ActivePlotNodeActions.ManagedTypeName),
        ScalingWeight = RequireLoadedType(KnownEntities.CompletionScalingWeight.ManagedTypeName),
        RewardPool = RequireLoadedType(KnownEntities.FruitTreeRewardPool.ManagedTypeName),
        ScalingWeightEffectMod = RequireLoadedExactType("ScalingWeightEffectMod"),
        TreasurePoolEffect = RequireLoadedExactType("TreasurePoolSO+TreasurePoolInstantEffect"),
    };

    internal static Type RequireLoadedType(string name) =>
        ReflectionUtil.FindLoadedType(name) ??
        throw new AutoHarvestRegistryNotReadyException($"native type {name} is not registered yet");

    internal static Type RequireLoadedExactType(string fullName)
    {
        var type = Type.GetType($"{fullName}, Assembly-CSharp", throwOnError: false);
        if (type is null || !string.Equals(type.FullName, fullName, StringComparison.Ordinal))
            throw new InvalidOperationException($"native type {fullName} is not registered exactly");
        return type;
    }
}

internal sealed class AutoHarvestReflectionContract
{
    private AutoHarvestReflectionContract(AutoHarvestReflectionTypes types)
    {
        Types = types;
    }

    public AutoHarvestReflectionTypes Types { get; }
    public Type InstantEffectBlockType { get; private set; } = null!;
    public Type PhaseInfoType { get; private set; } = null!;
    public AutoHarvestStableIdAccessor PlotStableId { get; private set; } = null!;
    public AutoHarvestStableIdAccessor ActionStableId { get; private set; } = null!;

    public FieldInfo PlotAvailableActions { get; private set; } = null!;
    public FieldInfo PlotPhaseInfos { get; private set; } = null!;
    public FieldInfo PlotAutoAction { get; private set; } = null!;
    public MethodInfo PlotIsVisible { get; private set; } = null!;
    public MethodInfo PlotGetActionInstances { get; private set; } = null!;
    public MethodInfo PlotGetRemainingQuantity { get; private set; } = null!;

    public FieldInfo ActionPrerequisites { get; private set; } = null!;
    public FieldInfo ActionIsGrowing { get; private set; } = null!;
    public FieldInfo ActionCostType { get; private set; } = null!;
    public FieldInfo ActionCostExitPhase { get; private set; } = null!;
    public FieldInfo ActionElementCost { get; private set; } = null!;
    public FieldInfo ActionUseSizeModForCost { get; private set; } = null!;
    public FieldInfo ActionUseAnyStateForCost { get; private set; } = null!;
    public FieldInfo ActionParallel { get; private set; } = null!;
    public FieldInfo ActionBaseTime { get; private set; } = null!;
    public FieldInfo ActionUseSpaceForTime { get; private set; } = null!;
    public FieldInfo ActionDrain { get; private set; } = null!;
    public FieldInfo ActionEffects { get; private set; } = null!;
    public FieldInfo ActionIgnoreYield { get; private set; } = null!;
    public FieldInfo ActionCompleteEffects { get; private set; } = null!;
    public MethodInfo ActionGetElementCost { get; private set; } = null!;

    public MethodInfo InstanceGetAction { get; private set; } = null!;
    public MethodInfo InstanceGetElement { get; private set; } = null!;
    public MethodInfo InstanceIsVisible { get; private set; } = null!;
    public MethodInfo InstanceIsEmpty { get; private set; } = null!;
    public MethodInfo InstanceIsEngaged { get; private set; } = null!;
    public MethodInfo InstanceHasEnough { get; private set; } = null!;
    public MethodInfo InstanceGetMaximumRemaining { get; private set; } = null!;
    public MethodInfo InstanceGetActualQuantity { get; private set; } = null!;

    public FieldInfo ActiveValues { get; private set; } = null!;
    public MethodInfo ActiveGetUsedSpots { get; private set; } = null!;
    public MethodInfo ActiveHasEmptySpot { get; private set; } = null!;
    public MethodInfo ActiveAddInstance { get; private set; } = null!;

    public FieldInfo PrerequisiteValues { get; private set; } = null!;
    public FieldInfo ResourceCosts { get; private set; } = null!;
    public FieldInfo EffectBlockPrerequisites { get; private set; } = null!;
    public FieldInfo EffectBlockMods { get; private set; } = null!;
    public FieldInfo InstantEffectScripts { get; private set; } = null!;
    public FieldInfo ScalingWeightRef { get; private set; } = null!;
    public FieldInfo ScalingWeight { get; private set; } = null!;
    public FieldInfo TreasurePool { get; private set; } = null!;
    public FieldInfo EffectType { get; private set; } = null!;
    public FieldInfo EffectValue { get; private set; } = null!;
    public FieldInfo FilterScaling { get; private set; } = null!;
    public FieldInfo FilterListType { get; private set; } = null!;
    public FieldInfo FilterListContents { get; private set; } = null!;
    public FieldInfo PhaseInfoPhase { get; private set; } = null!;
    public FieldInfo PhaseInfoTime { get; private set; } = null!;
    public FieldInfo PhaseInfoProcessType { get; private set; } = null!;
    public FieldInfo PhaseInfoExitPhase { get; private set; } = null!;

    public static AutoHarvestReflectionContract Bind(AutoHarvestReflectionTypes types)
    {
        var contract = new AutoHarvestReflectionContract(types);
        var phaseInfoType = AutoHarvestReflectionTypes.RequireLoadedExactType("PlotNodeSO+PlotNodePhaseInfo");
        var plotPhaseType = AutoHarvestReflectionTypes.RequireLoadedExactType("PlotNodeSO+PlotNodePhases");
        var timerType = AutoHarvestReflectionTypes.RequireLoadedExactType("TimerList+TimerType");
        var actionCostType = AutoHarvestReflectionTypes.RequireLoadedExactType("PlotNodeActionSO+CostType");
        var prerequisiteType = AutoHarvestReflectionTypes.RequireLoadedExactType("Prerequisites+Container");
        var requirementType = AutoHarvestReflectionTypes.RequireLoadedExactType("Requirements.IRequirementCondition");
        var resourceCostType = AutoHarvestReflectionTypes.RequireLoadedExactType("ResourceCostList");
        var resourceTupleType = AutoHarvestReflectionTypes.RequireLoadedExactType("ResourceTuple");
        var persistentEffectBlockType = AutoHarvestReflectionTypes.RequireLoadedExactType("PersistentEffectBlock");
        var instantEffectBlockType = AutoHarvestReflectionTypes.RequireLoadedExactType("InstantEffectBlock");
        var effectModType = AutoHarvestReflectionTypes.RequireLoadedExactType("IEffectMod");
        var instantEffectScriptType = AutoHarvestReflectionTypes.RequireLoadedExactType("IInstantEffectScript");
        var scalingWeightRefType = AutoHarvestReflectionTypes.RequireLoadedExactType("ScalingWeightRef");
        var filterEffectType = AutoHarvestReflectionTypes.RequireLoadedExactType("FilterEffectMod");
        var filterListType = AutoHarvestReflectionTypes.RequireLoadedExactType("FilterEffectMod+FilterType");
        var scalingType = AutoHarvestReflectionTypes.RequireLoadedExactType("ScalingType");

        contract.PhaseInfoType = phaseInfoType;
        contract.InstantEffectBlockType = instantEffectBlockType;
        contract.PlotStableId = AutoHarvestStableIdAccessor.Bind(types.Plot);
        contract.ActionStableId = AutoHarvestStableIdAccessor.Bind(types.Action);
        contract.PlotAvailableActions = RequireListField(types.Plot, "availableActions", types.Action);
        contract.PlotPhaseInfos = RequireListField(types.Plot, "phaseInfos", phaseInfoType);
        contract.PlotAutoAction = RequireField(types.Plot, "autoAction", types.Action);
        contract.PlotIsVisible = RequireMethod(types.Plot, "IsVisible", typeof(bool));
        contract.PlotGetActionInstances = RequireListMethod(types.Plot, "GetActionInstances", types.Instance);
        contract.PlotGetRemainingQuantity = RequireMethod(types.Plot, "GetRemainingQuantity", typeof(int));

        contract.ActionPrerequisites = RequireField(types.Action, "prerequisites", prerequisiteType);
        contract.ActionIsGrowing = RequireField(types.Action, "isGrowingAction", typeof(bool));
        contract.ActionCostType = RequireField(types.Action, "elementCostType", actionCostType);
        contract.ActionCostExitPhase = RequireField(types.Action, "elementCostExitPhase", plotPhaseType);
        contract.ActionElementCost = RequireField(types.Action, "elementCost", typeof(int));
        contract.ActionUseSizeModForCost = RequireField(types.Action, "useSizeModForCost", typeof(bool));
        contract.ActionUseAnyStateForCost = RequireField(types.Action, "useAnyStateForCost", typeof(bool));
        contract.ActionParallel = RequireField(types.Action, "parallelAction", typeof(bool));
        contract.ActionBaseTime = RequireField(types.Action, "baseTime", typeof(double));
        contract.ActionUseSpaceForTime = RequireField(types.Action, "useSpaceUsageForTimeMult", typeof(bool));
        contract.ActionDrain = RequireField(types.Action, "actionDrain", resourceCostType);
        contract.ActionEffects = RequireListField(types.Action, "actionEffects", persistentEffectBlockType);
        contract.ActionIgnoreYield = RequireField(types.Action, "ignoreNodeYield", typeof(bool));
        contract.ActionCompleteEffects = RequireListField(types.Action, "completeEffects", instantEffectBlockType);
        contract.ActionGetElementCost = RequireMethod(types.Action, "GetElementCost", typeof(int), types.Plot);

        contract.InstanceGetAction = RequireMethod(types.Instance, "GetAction", types.Action);
        contract.InstanceGetElement = RequireMethod(types.Instance, "GetElement", types.Plot);
        contract.InstanceIsVisible = RequireMethod(types.Instance, "IsVisible", typeof(bool));
        contract.InstanceIsEmpty = RequireMethod(types.Instance, "IsEmpty", typeof(bool));
        contract.InstanceIsEngaged = RequireMethod(types.Instance, "IsEngaged", typeof(bool));
        contract.InstanceHasEnough = RequireMethod(types.Instance, "HasEnoughForOneInstance", typeof(bool));
        contract.InstanceGetMaximumRemaining = RequireMethod(types.Instance, "GetMaximumRemInstances", typeof(int));
        contract.InstanceGetActualQuantity = RequireMethod(types.Instance, "GetActualQuantity", typeof(int));

        contract.ActiveValues = RequireListField(types.ActiveActions, "value", types.Instance);
        contract.ActiveGetUsedSpots = RequireMethod(types.ActiveActions, "GetUsedSpots", typeof(int));
        contract.ActiveHasEmptySpot = RequireMethod(types.ActiveActions, "HasEmptySpot", typeof(bool));
        contract.ActiveAddInstance = RequireMethod(types.ActiveActions, "AddInstance", typeof(void), types.Instance, typeof(int));

        contract.PrerequisiteValues = RequireListField(prerequisiteType, "prerequisites", requirementType);
        contract.ResourceCosts = RequireListField(resourceCostType, "costs", resourceTupleType);
        contract.EffectBlockPrerequisites = RequireField(instantEffectBlockType, "prerequisites", prerequisiteType);
        contract.EffectBlockMods = RequireListField(instantEffectBlockType, "effectMods", effectModType);
        contract.InstantEffectScripts = RequireListField(instantEffectBlockType, "effectScripts", instantEffectScriptType);
        contract.ScalingWeightRef = RequireField(types.ScalingWeightEffectMod, "scalingWeightRef", scalingWeightRefType);
        contract.ScalingWeight = RequireField(scalingWeightRefType, "scalingWeight", types.ScalingWeight);
        contract.TreasurePool = RequireField(types.TreasurePoolEffect, "treasurePool", types.RewardPool);
        contract.EffectType = RequireField(types.TreasurePoolEffect, "effectType", typeof(string));
        contract.EffectValue = RequireField(types.TreasurePoolEffect, "effectValue", typeof(double));
        contract.FilterScaling = RequireField(types.TreasurePoolEffect, "filterScaling", filterEffectType);
        contract.FilterListType = RequireField(filterEffectType, "listType", filterListType);
        contract.FilterListContents = RequireListField(filterEffectType, "listContents", scalingType);

        contract.PhaseInfoPhase = RequireField(phaseInfoType, "phase", plotPhaseType);
        contract.PhaseInfoTime = RequireField(phaseInfoType, "phaseTime", typeof(double));
        contract.PhaseInfoProcessType = RequireField(phaseInfoType, "processType", timerType);
        contract.PhaseInfoExitPhase = RequireField(phaseInfoType, "exitPhase", plotPhaseType);
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

    private static FieldInfo RequireField(Type type, string name, Type expectedType)
    {
        var field = RequireField(type, name);
        if (field.FieldType != expectedType)
            throw new InvalidOperationException(
                $"{type.FullName}.{name} field type is {field.FieldType.FullName}; expected {expectedType.FullName}");
        return field;
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
