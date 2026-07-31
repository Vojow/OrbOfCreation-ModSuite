using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One recipe named by the native ConceptRecipes registry.</summary>
internal readonly struct WorldConceptRecipe
{
    internal WorldConceptRecipe(Guid recipeId, Guid coreTypeId, bool canAddNow = true)
    {
        RecipeId = recipeId;
        CoreTypeId = coreTypeId;
        CanAddNow = canAddNow;
    }

    internal Guid RecipeId { get; }

    /// <summary>The native slot family that prevents two incompatible concepts sharing one slot.</summary>
    internal Guid CoreTypeId { get; }

    /// <summary>Whether the authoritative Active Concepts list can admit this recipe now.</summary>
    internal bool CanAddNow { get; }
}

/// <summary>One active Concept assignment as it stood when the world was collected.</summary>
internal readonly struct WorldAlchemyInstance
{
    internal WorldAlchemyInstance(
        Guid recipeId,
        int quantity,
        int queuedQuantity,
        bool drainReadable,
        BigDouble drainRatio)
    {
        RecipeId = recipeId;
        Quantity = Math.Max(0, quantity);
        QueuedQuantity = Math.Max(0, queuedQuantity);
        DrainReadable = drainReadable;
        DrainRatio = drainRatio;
    }

    internal Guid RecipeId { get; }
    internal int Quantity { get; }
    internal int QueuedQuantity { get; }
    internal bool IsSettled => Quantity == QueuedQuantity;

    /// <summary>
    /// Whether both the native ratio and current drain vector were readable. False is deliberately
    /// unsafe: the rollback watchdog may not turn missing evidence into permission to keep draining.
    /// </summary>
    internal bool DrainReadable { get; }
    internal BigDouble DrainRatio { get; }
}

internal enum WorldAlchemyCostKind
{
    RecipeDrain = 0,
    CurrentDrain = 1,
}

internal static class WorldConceptRecipeLookup
{
    internal static bool TryFind(
        PublicationTable<WorldConceptRecipe> table,
        Guid recipeId,
        out WorldConceptRecipe recipe) =>
        WorldAlchemyRowLookup.TryFind(table, recipeId, static row => row.RecipeId, out recipe);
}

internal static class WorldAlchemyInstanceLookup
{
    internal static bool TryFind(
        PublicationTable<WorldAlchemyInstance> table,
        Guid recipeId,
        out WorldAlchemyInstance instance) =>
        WorldAlchemyRowLookup.TryFind(table, recipeId, static row => row.RecipeId, out instance);
}

internal static class WorldAlchemyRowLookup
{
    internal static bool TryFind<TRow>(
        PublicationTable<TRow> table,
        Guid recipeId,
        Func<TRow, Guid> readId,
        out TRow row)
        where TRow : struct
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = readId(rows[middle]).CompareTo(recipeId);
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
}

internal sealed class WorldConceptRecipeBuffer
{
    private WorldConceptRecipe[] _samples = new WorldConceptRecipe[32];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldConceptRecipe this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldConceptRecipe sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal sealed class WorldAlchemyInstanceBuffer
{
    private WorldAlchemyInstance[] _samples = new WorldAlchemyInstance[16];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldAlchemyInstance this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldAlchemyInstance sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldAlchemyRowDeriver
{
    internal static PublicationTable<WorldConceptRecipe> Build(WorldConceptRecipeBuffer buffer)
    {
        var rows = new WorldConceptRecipe[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        return PublicationTable<WorldConceptRecipe>.Create(rows, rows.Length);
    }

    internal static PublicationTable<WorldAlchemyInstance> Build(WorldAlchemyInstanceBuffer buffer)
    {
        var rows = new WorldAlchemyInstance[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        return PublicationTable<WorldAlchemyInstance>.Create(rows, rows.Length);
    }
}

/// <summary>A single resource row in either a Concept recipe's authored or current drain vector.</summary>
internal readonly struct WorldAlchemyCost
{
    internal WorldAlchemyCost(
        Guid recipeId,
        WorldAlchemyCostKind kind,
        Guid resourceId,
        BigDouble amount)
    {
        RecipeId = recipeId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid RecipeId { get; }
    internal WorldAlchemyCostKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal static class WorldAlchemyCostLookup
{
    internal static bool TryFindRange(
        PublicationTable<WorldAlchemyCost> table,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(rows[middle].RecipeId, rows[middle].Kind, recipeId, kind);
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(rows[start + count].RecipeId, rows[start + count].Kind, recipeId, kind) == 0)
        {
            count++;
        }

        return count > 0;
    }

    private static int Compare(
        Guid leftRecipe,
        WorldAlchemyCostKind leftKind,
        Guid rightRecipe,
        WorldAlchemyCostKind rightKind)
    {
        var byRecipe = leftRecipe.CompareTo(rightRecipe);
        return byRecipe != 0 ? byRecipe : ((int)leftKind).CompareTo((int)rightKind);
    }
}

internal sealed class WorldAlchemyCostBuffer
{
    private const int InitialCapacity = 64;
    private WorldAlchemyCost[] _samples = new WorldAlchemyCost[InitialCapacity];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldAlchemyCost this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldAlchemyCost sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldAlchemyCostDeriver
{
    internal static PublicationTable<WorldAlchemyCost> Build(WorldAlchemyCostBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldAlchemyCost>.Empty;

        var rows = new WorldAlchemyCost[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, CostComparer.Instance);
        return PublicationTable<WorldAlchemyCost>.Create(rows, rows.Length);
    }

    private sealed class CostComparer : IComparer<WorldAlchemyCost>
    {
        internal static readonly IComparer<WorldAlchemyCost> Instance = new CostComparer();

        public int Compare(WorldAlchemyCost left, WorldAlchemyCost right)
        {
            var byRecipe = left.RecipeId.CompareTo(right.RecipeId);
            if (byRecipe != 0) return byRecipe;
            var byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            return byKind != 0 ? byKind : left.ResourceId.CompareTo(right.ResourceId);
        }
    }
}

/// <summary>
/// Reads the two Concept registries together: the scoped recipes and their authored drains, followed
/// by the active instances and their current drains.
/// </summary>
internal sealed class WorldAlchemyInstanceReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _registryType;
    private readonly Type? _activeListType;
    private readonly Type? _recipeListType;
    private readonly Type? _recipeType;
    private readonly Type? _instanceType;
    private readonly string _unavailable;

    private readonly Func<object, IList?>? _activeValues;
    private readonly Func<object, IList?>? _recipeValues;
    private readonly MethodInfo? _canAddInstance;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, Guid>? _coreTypeId;
    private readonly Func<object, object?>? _recipeDrain;
    private readonly Func<object, Guid>? _instanceRecipeId;
    private readonly Func<object, int>? _quantity;
    private readonly Func<object, int>? _queuedQuantity;
    private readonly Func<object, object?>? _resourceDrain;
    private readonly Func<object, BigDouble>? _drainRatio;
    private readonly MethodInfo? _currentDrain;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _entryResourceId;
    private readonly Func<object, BigDouble>? _entryAmount;

    internal WorldAlchemyInstanceReader(
        Type? registryType,
        Type? activeListType,
        Type? recipeListType,
        Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _registryType = registryType;
        _activeListType = activeListType;
        _recipeListType = recipeListType;
        _recipeType = resolveType("AlchemyRecipeSO");

        if (registryType is null || activeListType is null || recipeListType is null || _recipeType is null)
        {
            _unavailable = "the Concept registry types were not found on this build";
            return;
        }

        _activeValues = NativeAccessorBinder.CollectionField(activeListType, "value");
        _recipeValues = NativeAccessorBinder.CollectionField(recipeListType, "value");
        _canAddInstance = activeListType.GetMethod(
            "CanAddInstance", Instance, null, new[] { _recipeType! }, null);
        if (_canAddInstance?.ReturnType != typeof(bool)) _canAddInstance = null;
        _instanceType = NativeAccessorBinder.CollectionElementType(activeListType, "value");
        _recipeId = NativeAccessorBinder.Call<Guid>(_recipeType, "GetGuid");
        _coreTypeId = NativeAccessorBinder.CallReferenceGuid(_recipeType, "GetCoreType");
        _recipeDrain = NativeAccessorBinder.Reference(_recipeType, "drainCost");

        _instanceRecipeId = NativeAccessorBinder.CallReferenceGuid(_instanceType, "get_reference");
        _quantity = NativeAccessorBinder.Field<int>(_instanceType, "quantity");
        _queuedQuantity = NativeAccessorBinder.Field<int>(_instanceType, "queuedQuantity");
        _resourceDrain = NativeAccessorBinder.Reference(_instanceType, "resourceDrain");

        var drainType = _instanceType?.GetField("resourceDrain", Instance)?.FieldType;
        _drainRatio = NativeAccessorBinder.Call<BigDouble>(drainType, "GetRatio");
        _currentDrain = drainType?.GetMethod("GetCurrentDrain", Instance, null, Type.EmptyTypes, null);

        var costListType = _recipeType.GetField("drainCost", Instance)?.FieldType;
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _costEntries = NativeAccessorBinder.CollectionField(costListType, "costs");
        _entryResourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _entryAmount = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");

        _unavailable = IsBound()
            ? string.Empty
            : "the active Concept instance or drain-vector members were unavailable";
    }

    public string Category => "concept instances";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.ConceptRecipes.Reset();
        frame.AlchemyInstances.Reset();
        frame.AlchemyCosts.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var registry = NativeAccessorBinder.StaticDictionary(_registryType, "RuntimeLookup");
        if (registry is null)
            return WorldCategoryReport.Missing(Category, "the identity registry was unreadable");

        var recipeList = registry[KnownEntities.ConceptRecipes.Uuid];
        var activeList = registry[KnownEntities.ActiveConcepts.Uuid];
        if (recipeList is null || activeList is null)
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
        if (recipeList.GetType() != _recipeListType || activeList.GetType() != _activeListType)
            return WorldCategoryReport.Missing(Category, "a Concept registry held an unexpected native type");

        try
        {
            var recipes = _recipeValues!(recipeList);
            var active = _activeValues!(activeList);
            var conceptIds = new HashSet<Guid>();
            var sampled = 0;
            var skipped = 0;
            var firstFailure = string.Empty;

            for (var index = 0; index < (recipes?.Count ?? 0); index++)
            {
                var recipe = recipes![index];
                if (recipe is null || recipe.GetType() != _recipeType)
                {
                    Skip(ref skipped, ref firstFailure, $"recipe {index} had an unexpected native type");
                    continue;
                }

                var id = _recipeId!(recipe);
                var core = _coreTypeId!(recipe);
                if (id == Guid.Empty || core == Guid.Empty || !conceptIds.Add(id))
                {
                    Skip(ref skipped, ref firstFailure, $"recipe {index} had an invalid identity or core type");
                    continue;
                }

                var canAddValue = _canAddInstance!.Invoke(activeList, new[] { recipe });
                if (canAddValue is not bool canAddNow)
                    throw new InvalidOperationException(
                        "AlchemyInstanceListVariable.CanAddInstance returned no Boolean value");
                frame.ConceptRecipes.Append(new WorldConceptRecipe(id, core, canAddNow));
                AppendCosts(id, WorldAlchemyCostKind.RecipeDrain, _recipeDrain!(recipe), frame.AlchemyCosts);
                sampled++;
            }

            for (var index = 0; index < (active?.Count ?? 0); index++)
            {
                var instance = active![index];
                if (instance is null || instance.GetType() != _instanceType)
                {
                    Skip(ref skipped, ref firstFailure, $"active instance {index} had an unexpected native type");
                    continue;
                }

                var id = _instanceRecipeId!(instance);
                if (!conceptIds.Contains(id))
                {
                    Skip(ref skipped, ref firstFailure, $"active instance {index} did not name a scoped Concept recipe");
                    continue;
                }

                var drain = _resourceDrain!(instance);
                var current = drain is null ? null : _currentDrain!.Invoke(drain, null);
                var readable = drain is not null && current is not null;
                var ratio = readable ? _drainRatio!(drain!) : default;
                if (readable)
                    AppendCosts(id, WorldAlchemyCostKind.CurrentDrain, current, frame.AlchemyCosts);
                frame.AlchemyInstances.Append(new WorldAlchemyInstance(
                    id, _quantity!(instance), _queuedQuantity!(instance), readable, ratio));
            }

            return new WorldCategoryReport(
                Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
        {
            return WorldCategoryReport.Missing(
                Category, $"reading Concept instances threw: {ex.GetBaseException().Message}");
        }
    }

    private void AppendCosts(
        Guid recipeId,
        WorldAlchemyCostKind kind,
        object? costList,
        WorldAlchemyCostBuffer destination)
    {
        if (costList is null) return;
        var entries = _costEntries!(costList);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resourceId = _entryResourceId!(entry);
            if (resourceId == Guid.Empty) continue;
            destination.Append(new WorldAlchemyCost(
                recipeId, kind, resourceId, _entryAmount!(entry)));
        }
    }

    private bool IsBound() =>
        _activeValues is not null && _recipeValues is not null && _canAddInstance is not null &&
        _instanceType is not null &&
        _recipeId is not null && _coreTypeId is not null && _recipeDrain is not null &&
        _instanceRecipeId is not null && _quantity is not null && _queuedQuantity is not null &&
        _resourceDrain is not null && _drainRatio is not null && _currentDrain is not null &&
        _costEntries is not null && _entryResourceId is not null && _entryAmount is not null;

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
