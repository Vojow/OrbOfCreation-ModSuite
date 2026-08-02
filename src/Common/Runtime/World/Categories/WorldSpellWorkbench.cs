using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldSpellWorkbench
{
    internal WorldSpellWorkbench(
        PublicationTable<WorldSpellWorkbenchGlyph> coreGlyphs,
        PublicationTable<WorldSpellWorkbenchGlyph> augmentGlyphs,
        PublicationTable<WorldSpellWorkbenchCost> creationCosts,
        bool creationAffordable,
        int equippedCount,
        int maximumEquipped,
        bool hasEmptySlot)
    {
        CoreGlyphs = coreGlyphs ?? throw new ArgumentNullException(nameof(coreGlyphs));
        AugmentGlyphs = augmentGlyphs ?? throw new ArgumentNullException(nameof(augmentGlyphs));
        CreationCosts = creationCosts ?? throw new ArgumentNullException(nameof(creationCosts));
        CreationAffordable = creationAffordable;
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
    }

    internal PublicationTable<WorldSpellWorkbenchGlyph> CoreGlyphs { get; }
    internal PublicationTable<WorldSpellWorkbenchGlyph> AugmentGlyphs { get; }
    internal PublicationTable<WorldSpellWorkbenchCost> CreationCosts { get; }
    internal bool CreationAffordable { get; }
    internal int EquippedCount { get; }
    internal int MaximumEquipped { get; }
    internal bool HasEmptySlot { get; }
}

internal readonly struct WorldSpellWorkbenchCost
{
    internal WorldSpellWorkbenchCost(Guid resourceId, BigDouble cost, BigDouble availableAmount)
    {
        ResourceId = resourceId;
        Cost = cost;
        AvailableAmount = availableAmount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
    internal BigDouble AvailableAmount { get; }
}

internal readonly struct WorldSpellWorkbenchGlyph
{
    internal WorldSpellWorkbenchGlyph(int position, Guid glyphId)
    {
        Position = position;
        GlyphId = glyphId;
    }

    internal int Position { get; }
    internal Guid GlyphId { get; }
}

internal sealed class WorldSpellWorkbenchBuffer
{
    private WorldSpellWorkbenchGlyph[] _core = new WorldSpellWorkbenchGlyph[8];
    private WorldSpellWorkbenchGlyph[] _augments = new WorldSpellWorkbenchGlyph[8];
    private WorldSpellWorkbenchCost[] _costs = new WorldSpellWorkbenchCost[8];
    private int _coreCount;
    private int _augmentCount;
    private int _costCount;

    internal int CoreCount => _coreCount;
    internal int AugmentCount => _augmentCount;
    internal int CostCount => _costCount;
    internal bool CreationAffordable { get; private set; }
    internal int EquippedCount { get; private set; }
    internal int MaximumEquipped { get; private set; }
    internal bool HasEmptySlot { get; private set; }

    internal void Reset()
    {
        _coreCount = 0;
        _augmentCount = 0;
        _costCount = 0;
        CreationAffordable = false;
        EquippedCount = 0;
        MaximumEquipped = 0;
        HasEmptySlot = false;
    }

    internal void AppendCore(Guid id) => Append(ref _core, ref _coreCount, id);
    internal void AppendAugment(Guid id) => Append(ref _augments, ref _augmentCount, id);

    internal void AppendCost(Guid resourceId, BigDouble cost, BigDouble availableAmount)
    {
        if (_costCount >= _costs.Length) Array.Resize(ref _costs, _costs.Length * 2);
        _costs[_costCount++] = new WorldSpellWorkbenchCost(resourceId, cost, availableAmount);
    }

    internal void SetCreationAffordable(bool value) => CreationAffordable = value;

    internal void SetCapacity(int equippedCount, int maximumEquipped, bool hasEmptySlot)
    {
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
    }

    internal WorldSpellWorkbench Build() => new(
        PublicationTable<WorldSpellWorkbenchGlyph>.Create(_core, CoreCount),
        PublicationTable<WorldSpellWorkbenchGlyph>.Create(_augments, AugmentCount),
        PublicationTable<WorldSpellWorkbenchCost>.Create(_costs, CostCount),
        CreationAffordable,
        EquippedCount,
        MaximumEquipped,
        HasEmptySlot);

    private static void Append(
        ref WorldSpellWorkbenchGlyph[] values,
        ref int count,
        Guid id)
    {
        if (count >= values.Length) Array.Resize(ref values, values.Length * 2);
        values[count] = new WorldSpellWorkbenchGlyph(count, id);
        count++;
    }
}

/// <summary>One read-only, main-thread capture of the native spell workbench selection and loadout room.</summary>
internal sealed class WorldSpellWorkbenchReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _managerType;
    private readonly Type? _glyphType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _core;
    private readonly Func<object, object?>? _augments;
    private readonly Func<object, object?>? _active;
    private readonly Func<object, IList?>? _glyphValues;
    private readonly Func<object, Guid>? _glyphId;
    private readonly Func<object, int>? _used;
    private readonly Func<object, int>? _maximum;
    private readonly Func<object, bool>? _hasEmpty;
    private readonly Func<object, object, object?>? _getCreateCost;
    private readonly Func<object, bool>? _costAffordable;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResourceId;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, BigDouble>? _resourceAmount;
    private readonly string _unavailable;

    internal WorldSpellWorkbenchReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _managerType = resolveType("SpellManager");
        _glyphType = resolveType("GlyphSO");
        var glyphListType = resolveType("GlyphListVariable");
        var spellListType = resolveType("SpellListVariable");
        _manager = BindStaticReference(_managerType, "instance", _managerType);
        _core = NativeAccessorBinder.Reference(_managerType, "selectedCoreGlyphs", glyphListType);
        _augments = NativeAccessorBinder.Reference(_managerType, "selectedAugmentGlyphs", glyphListType);
        _active = NativeAccessorBinder.Reference(_managerType, "activeSpells", spellListType);
        _glyphValues = NativeAccessorBinder.CollectionField(glyphListType, "value");
        _glyphId = NativeAccessorBinder.Call<Guid>(_glyphType, "GetGuid");
        _used = NativeAccessorBinder.Call<int>(spellListType, "GetUsedSpots");
        _maximum = NativeAccessorBinder.Call<int>(spellListType, "GetMax");
        _hasEmpty = NativeAccessorBinder.Call<bool>(spellListType, "HasEmptySpot");
        var createCostMethod = _managerType?.GetMethod(
            "GetSpellCreateCost",
            Instance,
            null,
            glyphListType is null
                ? Type.EmptyTypes
                : new[] { typeof(List<>).MakeGenericType(_glyphType!) },
            null);
        var costType = createCostMethod?.ReturnType;
        _getCreateCost = BindObjectCall(createCostMethod);
        _costAffordable = NativeAccessorBinder.Call<bool>(costType, "HasEnough");
        var entryMethod = costType?.GetMethod("GetEntries", Instance, null, Type.EmptyTypes, null);
        var entryType = entryMethod?.ReturnType is { IsGenericType: true } entries
            ? entries.GetGenericArguments()[0]
            : null;
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", entryType);
        _costResourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _costValue = NativeAccessorBinder.Call<BigDouble>(entryType, "GetValue");
        var resourceType = entryType?.GetField("resource", Instance)?.FieldType;
        _costResource = NativeAccessorBinder.Reference(entryType, "resource", resourceType);
        _resourceAmount = NativeAccessorBinder.Call<BigDouble>(resourceType, "GetTrueQuantity");
        _unavailable = _managerType is null || _glyphType is null || glyphListType is null ||
            spellListType is null || _manager is null || _core is null || _augments is null ||
            _active is null || _glyphValues is null || _glyphId is null || _used is null ||
            _maximum is null || _hasEmpty is null || _getCreateCost is null ||
            _costAffordable is null || _costEntries is null || _costResourceId is null ||
            _costValue is null || _costResource is null || _resourceAmount is null
            ? "the complete SpellManager selection and loadout binding set was unavailable"
            : string.Empty;
    }

    public string Category => "spell workbench";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.SpellWorkbench;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var manager = _manager!();
            if (manager is null)
                return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
            var coreList = _core!(manager);
            Append(coreList, buffer.AppendCore);
            Append(_augments!(manager), buffer.AppendAugment);
            if (coreList is not null)
            {
                var cost = _getCreateCost!(manager, _glyphValues!(coreList)!);
                if (cost is not null)
                {
                    buffer.SetCreationAffordable(_costAffordable!(cost));
                    var entries = _costEntries!(cost);
                    for (var index = 0; index < (entries?.Count ?? 0); index++)
                    {
                        var entry = entries![index];
                        if (entry is null) continue;
                        var resource = _costResource!(entry);
                        buffer.AppendCost(
                            _costResourceId!(entry),
                            _costValue!(entry),
                            resource is null ? default : _resourceAmount!(resource));
                    }
                }
            }
            var active = _active!(manager);
            if (active is null)
                return WorldCategoryReport.Missing(Category, "SpellManager.activeSpells was null");
            buffer.SetCapacity(_used!(active), _maximum!(active), _hasEmpty!(active));
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);
        }
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category,
                "reading the native spell workbench threw: " + ex.GetBaseException().Message);
        }
    }

    private void Append(object? list, Action<Guid> append)
    {
        if (list is null) return;
        var values = _glyphValues!(list);
        for (var index = 0; index < (values?.Count ?? 0); index++)
        {
            var glyph = values![index];
            if (glyph is null || glyph.GetType() != _glyphType) continue;
            append(_glyphId!(glyph));
        }
    }

    private static Func<object?>? BindStaticReference(Type? owner, string name, Type? exactType)
    {
        if (owner is null || exactType is null) return null;
        var field = owner.GetField(name, Static);
        if (field is null || field.FieldType != exactType) return null;
        try
        {
            var read = Expression.Convert(Expression.Field(null, field), typeof(object));
            return Expression.Lambda<Func<object?>>(read).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<object, object, object?>? BindObjectCall(MethodInfo? method)
    {
        if (method is null || method.IsStatic || method.GetParameters().Length != 1 ||
            method.ReturnType.IsValueType)
        {
            return null;
        }
        try
        {
            var owner = Expression.Parameter(typeof(object), "owner");
            var argument = Expression.Parameter(typeof(object), "argument");
            var call = Expression.Convert(
                Expression.Call(
                    Expression.Convert(owner, method.DeclaringType!),
                    method,
                    Expression.Convert(argument, method.GetParameters()[0].ParameterType)),
                typeof(object));
            return Expression.Lambda<Func<object, object, object?>>(call, owner, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
