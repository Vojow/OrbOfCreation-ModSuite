using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum WorldHarvestActionCaptureState
{
    Unknown = 0,
    Complete = 1,
    ContractUnavailable = 2,
    Malformed = 3,
    LimitExceeded = 4,
}

/// <summary>One active Druidry action/element pair.</summary>
internal readonly struct WorldHarvestAction
{
    internal WorldHarvestAction(
        Guid actionId,
        Guid elementId,
        int currentLevel,
        int maximumLevel,
        bool visible,
        BigDouble actionCostModifier,
        BigDouble actionSpeed,
        bool hasInstanceScaling)
    {
        ActionId = actionId;
        ElementId = elementId;
        CurrentLevel = currentLevel;
        MaximumLevel = maximumLevel;
        Visible = visible;
        ActionCostModifier = actionCostModifier;
        ActionSpeed = actionSpeed;
        HasInstanceScaling = hasInstanceScaling;
    }

    internal Guid ActionId { get; }
    internal Guid ElementId { get; }
    internal int CurrentLevel { get; }
    internal int MaximumLevel { get; }
    internal bool Visible { get; }
    internal BigDouble ActionCostModifier { get; }
    internal BigDouble ActionSpeed { get; }
    internal bool HasInstanceScaling { get; }
}

internal enum WorldHarvestActionCostKind
{
    Base = 0,
    ObservedCurrent = 1,
}

/// <summary>
/// One raw cost tuple. Position is native list order and deliberately remains
/// part of the key so duplicate resource entries retain their arithmetic order.
/// </summary>
internal readonly struct WorldHarvestActionCost
{
    internal WorldHarvestActionCost(
        Guid actionId,
        Guid elementId,
        WorldHarvestActionCostKind kind,
        int position,
        Guid resourceId,
        BigDouble amount)
    {
        ActionId = actionId;
        ElementId = elementId;
        Kind = kind;
        Position = position;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ActionId { get; }
    internal Guid ElementId { get; }
    internal WorldHarvestActionCostKind Kind { get; }
    internal int Position { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal enum WorldHarvestActionScalingAxis
{
    Cost = 0,
    Speed = 1,
}

internal enum WorldHarvestActionModifierRole
{
    Modifier = 0,
    Exponent = 1,
}

/// <summary>One authored entry from an instance-scaling ValueModifierList.</summary>
internal readonly struct WorldHarvestActionModifier
{
    internal WorldHarvestActionModifier(
        Guid actionId,
        Guid elementId,
        WorldHarvestActionScalingAxis axis,
        WorldHarvestActionModifierRole role,
        int position,
        GameValueModifierType type,
        BigDouble amount,
        int order)
    {
        ActionId = actionId;
        ElementId = elementId;
        Axis = axis;
        Role = role;
        Position = position;
        Type = type;
        Amount = amount;
        Order = order;
    }

    internal Guid ActionId { get; }
    internal Guid ElementId { get; }
    internal WorldHarvestActionScalingAxis Axis { get; }
    internal WorldHarvestActionModifierRole Role { get; }
    internal int Position { get; }
    internal GameValueModifierType Type { get; }
    internal BigDouble Amount { get; }
    internal int Order { get; }
}

internal static class WorldHarvestActionLookup
{
    internal static bool TryFind(
        PublicationTable<WorldHarvestAction> table,
        Guid actionId,
        Guid elementId,
        out WorldHarvestAction row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(
                rows[middle].ActionId,
                rows[middle].ElementId,
                actionId,
                elementId);
            if (comparison == 0)
            {
                row = rows[middle];
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }

    internal static bool TryFindCosts(
        PublicationTable<WorldHarvestActionCost> table,
        Guid actionId,
        Guid elementId,
        WorldHarvestActionCostKind kind,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(
                rows[middle].ActionId,
                rows[middle].ElementId,
                rows[middle].Kind,
                actionId,
                elementId,
                kind);
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(
                   rows[start + count].ActionId,
                   rows[start + count].ElementId,
                   rows[start + count].Kind,
                   actionId,
                   elementId,
                   kind) == 0)
        {
            count++;
        }
        return count > 0;
    }

    internal static bool TryFindModifiers(
        PublicationTable<WorldHarvestActionModifier> table,
        Guid actionId,
        Guid elementId,
        WorldHarvestActionScalingAxis axis,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(
                rows[middle].ActionId,
                rows[middle].ElementId,
                rows[middle].Axis,
                actionId,
                elementId,
                axis);
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(
                   rows[start + count].ActionId,
                   rows[start + count].ElementId,
                   rows[start + count].Axis,
                   actionId,
                   elementId,
                   axis) == 0)
        {
            count++;
        }
        return count > 0;
    }

    private static int Compare(Guid leftAction, Guid leftElement, Guid rightAction, Guid rightElement)
    {
        var action = leftAction.CompareTo(rightAction);
        return action != 0 ? action : leftElement.CompareTo(rightElement);
    }

    private static int Compare(
        Guid leftAction,
        Guid leftElement,
        WorldHarvestActionCostKind leftKind,
        Guid rightAction,
        Guid rightElement,
        WorldHarvestActionCostKind rightKind)
    {
        var pair = Compare(leftAction, leftElement, rightAction, rightElement);
        return pair != 0 ? pair : ((int)leftKind).CompareTo((int)rightKind);
    }

    private static int Compare(
        Guid leftAction,
        Guid leftElement,
        WorldHarvestActionScalingAxis leftAxis,
        Guid rightAction,
        Guid rightElement,
        WorldHarvestActionScalingAxis rightAxis)
    {
        var pair = Compare(leftAction, leftElement, rightAction, rightElement);
        return pair != 0 ? pair : ((int)leftAxis).CompareTo((int)rightAxis);
    }
}

internal sealed class WorldHarvestActionBuffer
{
    private WorldHarvestAction[] _actions = new WorldHarvestAction[16];
    private WorldHarvestActionCost[] _costs = new WorldHarvestActionCost[64];
    private WorldHarvestActionModifier[] _modifiers = new WorldHarvestActionModifier[64];

    internal int ActionCount { get; private set; }
    internal int CostCount { get; private set; }
    internal int ModifierCount { get; private set; }
    internal WorldHarvestActionCaptureState State { get; set; }
    internal ref readonly WorldHarvestAction Action(int index) => ref _actions[index];
    internal ref readonly WorldHarvestActionCost Cost(int index) => ref _costs[index];
    internal ref readonly WorldHarvestActionModifier Modifier(int index) => ref _modifiers[index];

    internal void Reset(WorldHarvestActionCaptureState state = WorldHarvestActionCaptureState.Unknown)
    {
        ActionCount = 0;
        CostCount = 0;
        ModifierCount = 0;
        State = state;
    }

    internal void Append(in WorldHarvestAction row)
    {
        if (ActionCount == _actions.Length) Array.Resize(ref _actions, _actions.Length * 2);
        _actions[ActionCount++] = row;
    }

    internal void Append(in WorldHarvestActionCost row)
    {
        if (CostCount == _costs.Length) Array.Resize(ref _costs, _costs.Length * 2);
        _costs[CostCount++] = row;
    }

    internal void Append(in WorldHarvestActionModifier row)
    {
        if (ModifierCount == _modifiers.Length)
            Array.Resize(ref _modifiers, _modifiers.Length * 2);
        _modifiers[ModifierCount++] = row;
    }
}

internal static class WorldHarvestActionDeriver
{
    internal static PublicationTable<WorldHarvestAction> BuildActions(WorldHarvestActionBuffer buffer)
    {
        if (buffer.ActionCount == 0) return PublicationTable<WorldHarvestAction>.Empty;
        var rows = new WorldHarvestAction[buffer.ActionCount];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer.Action(index);
        Array.Sort(rows, static (left, right) =>
        {
            var action = left.ActionId.CompareTo(right.ActionId);
            return action != 0 ? action : left.ElementId.CompareTo(right.ElementId);
        });
        return PublicationTable<WorldHarvestAction>.Create(rows, rows.Length);
    }

    internal static PublicationTable<WorldHarvestActionCost> BuildCosts(WorldHarvestActionBuffer buffer)
    {
        if (buffer.CostCount == 0) return PublicationTable<WorldHarvestActionCost>.Empty;
        var rows = new WorldHarvestActionCost[buffer.CostCount];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer.Cost(index);
        Array.Sort(rows, static (left, right) =>
        {
            var action = left.ActionId.CompareTo(right.ActionId);
            if (action != 0) return action;
            var element = left.ElementId.CompareTo(right.ElementId);
            if (element != 0) return element;
            var kind = ((int)left.Kind).CompareTo((int)right.Kind);
            return kind != 0 ? kind : left.Position.CompareTo(right.Position);
        });
        return PublicationTable<WorldHarvestActionCost>.Create(rows, rows.Length);
    }

    internal static PublicationTable<WorldHarvestActionModifier> BuildModifiers(
        WorldHarvestActionBuffer buffer)
    {
        if (buffer.ModifierCount == 0)
            return PublicationTable<WorldHarvestActionModifier>.Empty;
        var rows = new WorldHarvestActionModifier[buffer.ModifierCount];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer.Modifier(index);
        Array.Sort(rows, static (left, right) =>
        {
            var action = left.ActionId.CompareTo(right.ActionId);
            if (action != 0) return action;
            var element = left.ElementId.CompareTo(right.ElementId);
            if (element != 0) return element;
            var axis = ((int)left.Axis).CompareTo((int)right.Axis);
            if (axis != 0) return axis;
            var role = ((int)left.Role).CompareTo((int)right.Role);
            return role != 0 ? role : left.Position.CompareTo(right.Position);
        });
        return PublicationTable<WorldHarvestActionModifier>.Create(rows, rows.Length);
    }
}

/// <summary>
/// Reads the active Druidry list as one atomic bounded category. Any malformed
/// pair clears every row from this pass, so an empty complete table and a
/// failed capture can never be confused.
/// </summary>
internal sealed class WorldHarvestActionReader : IWorldCategoryReader
{
    private const int MaximumPairs = 512;
    private const int MaximumCosts = 4096;
    private const int MaximumModifiers = 4096;
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _registryType;
    private readonly Type? _listType;
    private readonly Type? _instanceType;
    private readonly Type? _actionType;
    private readonly Type? _elementType;
    private readonly Type? _actionReferenceType;
    private readonly Type? _resourceType;
    private readonly Type? _modifierListType;
    private readonly string _unavailable;

    private readonly Func<object, IList?>? _activeValues;
    private readonly Func<object, Guid>? _listId;
    private readonly Func<object, int>? _level;
    private readonly Func<object, Guid>? _actionId;
    private readonly Func<object, Guid>? _elementId;
    private readonly Func<object, object?>? _actionCost;
    private readonly Func<object, double>? _elementCost;
    private readonly Func<object, object?>? _resourceDrain;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, object?>? _actionCostRecord;
    private readonly Func<object, object?>? _actionSpeedRecord;
    private readonly NativeModifierRecordAccess? _costRecord;
    private readonly NativeModifierRecordAccess? _speedRecord;
    private readonly Func<object, object?>? _instanceScaling;
    private readonly Func<object, object?>? _scaling;
    private readonly Func<object, object?>? _scalingConversion;
    private readonly Func<object, IList?>? _listModifiers;
    private readonly Func<object, IList?>? _listExponents;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, BigDouble>? _modifierAmount;
    private readonly Func<object, int>? _modifierOrder;

    private readonly MethodInfo? _getAction;
    private readonly MethodInfo? _getElement;
    private readonly MethodInfo? _getActionReference;
    private readonly MethodInfo? _isVisible;
    private readonly MethodInfo? _getMaximumInstances;
    private readonly MethodInfo? _getInternalResource;
    private readonly MethodInfo? _getCurrentDrain;
    private readonly MethodInfo? _getCostModifiers;
    private readonly MethodInfo? _getSpeedModifiers;

    internal WorldHarvestActionReader(
        Type? registryType,
        Type? listType,
        Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _registryType = registryType;
        _listType = listType;
        _instanceType = resolveType("HarvestActionInstance");
        _actionType = resolveType("HarvestActionSO");
        _elementType = resolveType("HarvestElementSO");
        _actionReferenceType = resolveType("HarvestElementSO+HarvestActionReference");
        _resourceType = resolveType("ResourceSO");
        var resourceDrainType = resolveType("ResourceDrain");
        var resourceCostListType = resolveType("ResourceCostList");
        var resourceTupleType = resolveType("ResourceTuple");
        var instanceScalingRefType = resolveType("InstanceScalingRef");
        var instanceScalingType = resolveType("InstanceScalingSO");
        var scalingConversionType = resolveType("ScalingConversion");
        _modifierListType = resolveType("ValueModifierList");

        if (registryType is null || listType is null || _instanceType is null ||
            _actionType is null || _elementType is null || _actionReferenceType is null ||
            _resourceType is null || resourceDrainType is null || resourceCostListType is null ||
            resourceTupleType is null || instanceScalingRefType is null ||
            instanceScalingType is null || scalingConversionType is null ||
            _modifierListType is null)
        {
            var missing = new List<string>();
            AddMissing(missing, "IdScriptableObject", registryType);
            AddMissing(missing, "HarvestActionInstanceListVariable", listType);
            AddMissing(missing, "HarvestActionInstance", _instanceType);
            AddMissing(missing, "HarvestActionSO", _actionType);
            AddMissing(missing, "HarvestElementSO", _elementType);
            AddMissing(
                missing,
                "HarvestElementSO+HarvestActionReference",
                _actionReferenceType);
            AddMissing(missing, "ResourceSO", _resourceType);
            AddMissing(missing, "ResourceDrain", resourceDrainType);
            AddMissing(missing, "ResourceCostList", resourceCostListType);
            AddMissing(missing, "ResourceTuple", resourceTupleType);
            AddMissing(missing, "InstanceScalingRef", instanceScalingRefType);
            AddMissing(missing, "InstanceScalingSO", instanceScalingType);
            AddMissing(missing, "ScalingConversion", scalingConversionType);
            AddMissing(missing, "ValueModifierList", _modifierListType);
            _unavailable =
                "the active Druidry native types were not found on this build: " +
                string.Join(", ", missing);
            return;
        }

        _activeValues = NativeAccessorBinder.CollectionField(listType, "value");
        _listId = NativeAccessorBinder.Call<Guid>(listType, "GetGuid");
        _level = NativeAccessorBinder.Field<int>(_instanceType, "instances");
        _actionId = NativeAccessorBinder.Call<Guid>(_actionType, "GetGuid");
        _elementId = NativeAccessorBinder.Call<Guid>(_elementType, "GetGuid");
        _getAction = ExactMethod(_instanceType, "GetAction", _actionType);
        _getElement = ExactMethod(_instanceType, "GetElement", _elementType);
        _getActionReference =
            ExactMethod(_instanceType, "GetActionRef", _actionReferenceType);
        _isVisible = ExactMethod(_instanceType, "IsVisible", typeof(bool));
        _getMaximumInstances =
            ExactMethod(_instanceType, "GetMaximumInstances", typeof(int));

        _actionCost = NativeAccessorBinder.Reference(_actionReferenceType, "actionCost");
        _elementCost = NativeAccessorBinder.Field<double>(_actionReferenceType, "elementCost");
        _resourceDrain = NativeAccessorBinder.Reference(_instanceType, "resourceDrain");
        _getCurrentDrain =
            ExactMethod(resourceDrainType, "GetCurrentDrain", resourceCostListType);
        _costEntries = NativeAccessorBinder.CollectionField(resourceCostListType, "costs");
        _costResource = NativeAccessorBinder.Reference(resourceTupleType, "resource");
        _costAmount = NativeAccessorBinder.Field<BigDouble>(resourceTupleType, "valueBig");
        _resourceId = NativeAccessorBinder.Call<Guid>(_resourceType, "GetGuid");
        _getInternalResource =
            ExactMethod(_elementType, "GetInternalResource", _resourceType);

        _actionCostRecord = NativeAccessorBinder.Reference(_actionType, "costMod");
        _actionSpeedRecord = NativeAccessorBinder.Reference(_actionType, "speed");
        _costRecord = NativeModifierRecordAccess.For(
            _actionType.GetField("costMod", Instance)?.FieldType);
        _speedRecord = NativeModifierRecordAccess.For(
            _actionType.GetField("speed", Instance)?.FieldType);

        _instanceScaling = NativeAccessorBinder.Reference(_actionType, "instanceScaling");
        _scaling = NativeAccessorBinder.Reference(instanceScalingRefType, "scaling");
        _scalingConversion =
            NativeAccessorBinder.Reference(instanceScalingType, "instanceScaling");
        _getCostModifiers =
            ExactMethod(scalingConversionType, "GetCostMod", _modifierListType);
        _getSpeedModifiers =
            ExactMethod(scalingConversionType, "GetSpeed", _modifierListType);
        _listModifiers = NativeAccessorBinder.CollectionField(_modifierListType, "modifiers");
        _listExponents = NativeAccessorBinder.CollectionField(_modifierListType, "exponents");
        var modifierType =
            NativeAccessorBinder.CollectionElementType(_modifierListType, "modifiers");
        _modifierType = NativeAccessorBinder.EnumField(modifierType, "type");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(modifierType, "adjustReal");
        _modifierOrder = NativeAccessorBinder.Field<int>(modifierType, "order");

        _unavailable = IsBound()
            ? string.Empty
            : "the active Druidry reader contract was unavailable";
    }

    public string Category => "active Druidry actions";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var destination = frame.HarvestActions;
        destination.Reset();
        if (!IsAvailable)
        {
            destination.State = WorldHarvestActionCaptureState.ContractUnavailable;
            return OptionalUnavailable(_unavailable);
        }

        var registry = NativeAccessorBinder.StaticDictionary(_registryType, "RuntimeLookup");
        if (registry is null)
            return Fail(destination, WorldHarvestActionCaptureState.ContractUnavailable,
                "the identity registry was unreadable");

        var activeList = registry[KnownEntities.ActiveHarvestActions.Uuid];
        if (activeList is null)
            return Fail(destination, WorldHarvestActionCaptureState.ContractUnavailable,
                "ActiveHarvestActions was absent from the identity registry");
        if (activeList.GetType() != _listType)
            return Fail(destination, WorldHarvestActionCaptureState.Malformed,
                "ActiveHarvestActions had an unexpected native type");
        var listId = _listId!(activeList);
        if (listId != KnownEntities.ActiveHarvestActions.Uuid)
            return Fail(destination, WorldHarvestActionCaptureState.Malformed,
                "ActiveHarvestActions carried an unexpected identity");
        if (!claimed.Add(listId))
            return Fail(destination, WorldHarvestActionCaptureState.Malformed,
                "ActiveHarvestActions identity appeared more than once");

        try
        {
            var active = _activeValues!(activeList);
            if (active is null)
                return Fail(destination, WorldHarvestActionCaptureState.Malformed,
                    "ActiveHarvestActions had no value list");
            if (active.Count > MaximumPairs)
                return Fail(destination, WorldHarvestActionCaptureState.LimitExceeded,
                    $"ActiveHarvestActions exceeded the {MaximumPairs}-pair capture limit");

            for (var index = 0; index < active.Count; index++)
            {
                var instance = active[index];
                if (instance is null || instance.GetType() != _instanceType)
                    return Malformed(destination, index, "had an unexpected native type");
                if (!Read(instance, destination, out var reason))
                    return Malformed(destination, index, reason);
                if (destination.CostCount > MaximumCosts ||
                    destination.ModifierCount > MaximumModifiers)
                {
                    return Fail(destination, WorldHarvestActionCaptureState.LimitExceeded,
                        "active Druidry rows exceeded the bounded capture limits");
                }
            }

            destination.State = WorldHarvestActionCaptureState.Complete;
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                destination.ActionCount,
                0,
                string.Empty);
        }
        catch (WorldHarvestActionLimitException exception)
        {
            return Fail(
                destination,
                WorldHarvestActionCaptureState.LimitExceeded,
                exception.Message);
        }
        catch (Exception exception) when (
            exception is TargetInvocationException or ArgumentException or
            InvalidOperationException or FormatException or OverflowException)
        {
            return Fail(
                destination,
                WorldHarvestActionCaptureState.Malformed,
                "reading active Druidry actions threw: " +
                exception.GetBaseException().Message);
        }
    }

    private bool Read(
        object instance,
        WorldHarvestActionBuffer destination,
        out string reason)
    {
        var action = _getAction!.Invoke(instance, null);
        var element = _getElement!.Invoke(instance, null);
        var actionReference = _getActionReference!.Invoke(instance, null);
        if (action is null || action.GetType() != _actionType ||
            element is null || element.GetType() != _elementType ||
            actionReference is null || actionReference.GetType() != _actionReferenceType)
            return Invalid("did not resolve an exact action/element pair", out reason);

        var actionId = _actionId!(action);
        var elementId = _elementId!(element);
        if (actionId == Guid.Empty || elementId == Guid.Empty ||
            Contains(destination, actionId, elementId))
            return Invalid("had a missing or duplicate pair identity", out reason);

        var level = _level!(instance);
        var maximumLevel = (int)_getMaximumInstances!.Invoke(instance, null)!;
        var visible = (bool)_isVisible!.Invoke(instance, null)!;
        if (level < 0 || maximumLevel <= 0 || level > maximumLevel)
            return Invalid("had an invalid current or maximum level", out reason);

        var actionCostRecord = _actionCostRecord!(action);
        var actionSpeedRecord = _actionSpeedRecord!(action);
        if (actionCostRecord is null || actionSpeedRecord is null)
            return Invalid("did not expose its action scaling records", out reason);
        var actionCostModifier = _costRecord!.Fold(actionCostRecord);
        var actionSpeed = _speedRecord!.Fold(actionSpeedRecord);
        if (!FiniteNonNegative(actionCostModifier) || !FiniteNonNegative(actionSpeed))
            return Invalid("had invalid resolved action scaling", out reason);

        var scalingReference = _instanceScaling!(action);
        if (scalingReference is null)
            return Invalid("did not expose its instance-scaling reference", out reason);
        var scaling = _scaling!(scalingReference);
        var hasInstanceScaling = scaling is not null;
        if (scaling is not null)
        {
            var conversion = _scalingConversion!(scaling);
            if (conversion is null)
                return Invalid("had a null instance-scaling conversion", out reason);
            var cost = _getCostModifiers!.Invoke(conversion, null);
            var speed = _getSpeedModifiers!.Invoke(conversion, null);
            if (cost is null || cost.GetType() != _modifierListType ||
                speed is null || speed.GetType() != _modifierListType)
                return Invalid("had invalid cost or speed instance scaling", out reason);
            if (!AppendModifiers(
                    actionId, elementId, WorldHarvestActionScalingAxis.Cost,
                    cost, destination, out reason) ||
                !AppendModifiers(
                    actionId, elementId, WorldHarvestActionScalingAxis.Speed,
                    speed, destination, out reason))
                return false;
        }

        if (!AppendCosts(
                actionId,
                elementId,
                WorldHarvestActionCostKind.Base,
                _actionCost!(actionReference),
                destination,
                out var baseCount,
                out reason))
            return false;

        var elementAmount = _elementCost!(actionReference);
        if (double.IsNaN(elementAmount) || double.IsInfinity(elementAmount) || elementAmount < 0)
            return Invalid("had an invalid element-internal-resource cost", out reason);
        if (elementAmount > 0)
        {
            var resource = _getInternalResource!.Invoke(element, null);
            if (resource is null || resource.GetType() != _resourceType)
                return Invalid("did not resolve its element-internal resource", out reason);
            var resourceId = _resourceId!(resource);
            if (resourceId == Guid.Empty)
                return Invalid("resolved an unidentified element-internal resource", out reason);
            EnsureCostCapacity(destination, 1);
            destination.Append(new WorldHarvestActionCost(
                actionId,
                elementId,
                WorldHarvestActionCostKind.Base,
                baseCount,
                resourceId,
                new BigDouble(elementAmount)));
        }

        var drain = _resourceDrain!(instance);
        if (level > 0 && drain is null)
            return Invalid("had no current resource drain", out reason);
        if (drain is not null)
        {
            var current = _getCurrentDrain!.Invoke(drain, null);
            if (current is null ||
                !AppendCosts(
                    actionId,
                    elementId,
                    WorldHarvestActionCostKind.ObservedCurrent,
                    current,
                    destination,
                    out _,
                    out reason))
                return false;
        }

        destination.Append(new WorldHarvestAction(
            actionId,
            elementId,
            level,
            maximumLevel,
            visible,
            actionCostModifier,
            actionSpeed,
            hasInstanceScaling));
        reason = string.Empty;
        return true;
    }

    private bool AppendCosts(
        Guid actionId,
        Guid elementId,
        WorldHarvestActionCostKind kind,
        object? costList,
        WorldHarvestActionBuffer destination,
        out int appended,
        out string reason)
    {
        appended = 0;
        if (costList is null)
            return Invalid("had a null native cost list", out reason);
        var entries = _costEntries!(costList);
        if (entries is null)
            return Invalid("had an unreadable native cost list", out reason);
        EnsureCostCapacity(destination, entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
                return Invalid("had a null native cost tuple", out reason);
            var resource = _costResource!(entry);
            if (resource is null || resource.GetType() != _resourceType)
                return Invalid("had a cost resource with an unexpected native type", out reason);
            var resourceId = _resourceId!(resource);
            var amount = _costAmount!(entry);
            if (resourceId == Guid.Empty || !FiniteNonNegative(amount))
                return Invalid("had an invalid native cost tuple", out reason);
            destination.Append(new WorldHarvestActionCost(
                actionId, elementId, kind, index, resourceId, amount));
            appended++;
        }

        reason = string.Empty;
        return true;
    }

    private bool AppendModifiers(
        Guid actionId,
        Guid elementId,
        WorldHarvestActionScalingAxis axis,
        object list,
        WorldHarvestActionBuffer destination,
        out string reason)
    {
        if (!AppendModifiers(
                actionId, elementId, axis, WorldHarvestActionModifierRole.Modifier,
                _listModifiers!(list), destination, out reason))
            return false;
        return AppendModifiers(
            actionId, elementId, axis, WorldHarvestActionModifierRole.Exponent,
            _listExponents!(list), destination, out reason);
    }

    private bool AppendModifiers(
        Guid actionId,
        Guid elementId,
        WorldHarvestActionScalingAxis axis,
        WorldHarvestActionModifierRole role,
        IList? source,
        WorldHarvestActionBuffer destination,
        out string reason)
    {
        if (source is null)
            return Invalid("had an unreadable instance-scaling modifier list", out reason);
        EnsureModifierCapacity(destination, source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            var modifier = source[index];
            if (modifier is null)
                return Invalid("had a null instance-scaling modifier", out reason);
            var type = _modifierType!(modifier);
            var amount = _modifierAmount!(modifier);
            if (type < (int)GameValueModifierType.Raw ||
                type > (int)GameValueModifierType.Exponent ||
                !Finite(amount))
                return Invalid("had an invalid instance-scaling modifier", out reason);
            destination.Append(new WorldHarvestActionModifier(
                actionId,
                elementId,
                axis,
                role,
                index,
                (GameValueModifierType)type,
                amount,
                _modifierOrder!(modifier)));
        }
        reason = string.Empty;
        return true;
    }

    private static void EnsureCostCapacity(
        WorldHarvestActionBuffer destination,
        int additional)
    {
        if (additional < 0 || additional > MaximumCosts - destination.CostCount)
            throw new WorldHarvestActionLimitException(
                $"active Druidry costs exceeded the {MaximumCosts}-row capture limit");
    }

    private static void EnsureModifierCapacity(
        WorldHarvestActionBuffer destination,
        int additional)
    {
        if (additional < 0 ||
            additional > MaximumModifiers - destination.ModifierCount)
        {
            throw new WorldHarvestActionLimitException(
                $"active Druidry modifiers exceeded the {MaximumModifiers}-row capture limit");
        }
    }

    private bool IsBound() =>
        _activeValues is not null && _listId is not null && _level is not null &&
        _actionId is not null && _elementId is not null &&
        _getAction is not null && _getElement is not null &&
        _getActionReference is not null && _isVisible is not null &&
        _getMaximumInstances is not null && _actionCost is not null &&
        _elementCost is not null && _resourceDrain is not null &&
        _getCurrentDrain is not null && _costEntries is not null &&
        _costResource is not null && _costAmount is not null &&
        _resourceId is not null && _getInternalResource is not null &&
        _actionCostRecord is not null && _actionSpeedRecord is not null &&
        _costRecord is not null && _speedRecord is not null &&
        _instanceScaling is not null && _scaling is not null &&
        _scalingConversion is not null && _getCostModifiers is not null &&
        _getSpeedModifiers is not null && _listModifiers is not null &&
        _listExponents is not null && _modifierType is not null &&
        _modifierAmount is not null && _modifierOrder is not null;

    private static MethodInfo? ExactMethod(
        Type owner,
        string name,
        Type returnType,
        params Type[] parameters)
    {
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        return method is not null && !method.IsStatic && method.ReturnType == returnType
            ? method
            : null;
    }

    private static void AddMissing(
        ICollection<string> missing,
        string name,
        Type? type)
    {
        if (type is null) missing.Add(name);
    }

    private static bool Contains(
        WorldHarvestActionBuffer buffer,
        Guid actionId,
        Guid elementId)
    {
        for (var index = 0; index < buffer.ActionCount; index++)
        {
            ref readonly var existing = ref buffer.Action(index);
            if (existing.ActionId == actionId && existing.ElementId == elementId)
                return true;
        }
        return false;
    }

    private static bool Finite(BigDouble value) =>
        !double.IsNaN(value.Mantissa) && !double.IsInfinity(value.Mantissa);

    private static bool FiniteNonNegative(BigDouble value) =>
        Finite(value) && value >= BigDouble.Zero;

    private static bool Invalid(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private WorldCategoryReport Malformed(
        WorldHarvestActionBuffer destination,
        int index,
        string reason) =>
        Fail(
            destination,
            WorldHarvestActionCaptureState.Malformed,
            $"active Druidry instance {index} {reason}");

    private WorldCategoryReport Fail(
        WorldHarvestActionBuffer destination,
        WorldHarvestActionCaptureState state,
        string reason)
    {
        destination.Reset(state);
        return state == WorldHarvestActionCaptureState.ContractUnavailable
            ? WorldCategoryReport.Missing(Category, reason)
            : new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled: 0,
                skipped: 1,
                reason);
    }

    private WorldCategoryReport OptionalUnavailable(string reason) =>
        WorldCategoryReport.Missing(Category, reason);

    private sealed class WorldHarvestActionLimitException : Exception
    {
        internal WorldHarvestActionLimitException(string message)
            : base(message)
        {
        }
    }
}
