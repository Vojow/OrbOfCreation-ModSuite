using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldScribeRecipe
{
    internal WorldScribeRecipe(Guid recipeId, Guid recipeTypeId, Guid outputConsumableId, bool visible, bool usesQuantityAsLevel)
    {
        RecipeId = recipeId; RecipeTypeId = recipeTypeId; OutputConsumableId = outputConsumableId;
        Visible = visible; UsesQuantityAsLevel = usesQuantityAsLevel;
    }
    internal Guid RecipeId { get; }
    internal Guid RecipeTypeId { get; }
    internal Guid OutputConsumableId { get; }
    internal bool Visible { get; }
    internal bool UsesQuantityAsLevel { get; }
}

internal readonly struct WorldScribeQueue
{
    internal WorldScribeQueue(Guid queueId, bool isAutomatic, int used, int maximum)
    {
        QueueId = queueId; IsAutomatic = isAutomatic; Used = used; Maximum = maximum;
    }
    internal Guid QueueId { get; }
    internal bool IsAutomatic { get; }
    internal int Used { get; }
    internal int Maximum { get; }
}

internal readonly struct WorldScribeWork
{
    internal WorldScribeWork(Guid queueId, Guid recipeId, int level, bool isAutomatic, bool isExpired)
    {
        QueueId = queueId; RecipeId = recipeId; Level = level;
        IsAutomatic = isAutomatic; IsExpired = isExpired;
    }
    internal Guid QueueId { get; }
    internal Guid RecipeId { get; }
    internal int Level { get; }
    internal bool IsAutomatic { get; }
    internal bool IsExpired { get; }
}

internal readonly struct WorldStructureEnchantment
{
    internal WorldStructureEnchantment(Guid structureId, Guid enchantmentId, int level)
    {
        StructureId = structureId; EnchantmentId = enchantmentId; Level = level;
    }
    internal Guid StructureId { get; }
    internal Guid EnchantmentId { get; }
    internal int Level { get; }
}

internal readonly struct WorldScrollTarget
{
    internal WorldScrollTarget(Guid consumableId, Guid enchantmentId, Guid structureId)
    {
        ConsumableId = consumableId; EnchantmentId = enchantmentId; StructureId = structureId;
    }
    internal Guid ConsumableId { get; }
    internal Guid EnchantmentId { get; }
    internal Guid StructureId { get; }
}

/// <summary>
/// Completeness marker for an accepted native Scroll target graph. A zero candidate count is a
/// meaningful complete result and must not be confused with a missing or unreadable relationship.
/// </summary>
internal readonly struct WorldScrollTargetEvidence
{
    internal WorldScrollTargetEvidence(
        Guid consumableId,
        Guid enchantmentId,
        int candidateCount)
    {
        ConsumableId = consumableId;
        EnchantmentId = enchantmentId;
        CandidateCount = candidateCount;
    }

    internal Guid ConsumableId { get; }
    internal Guid EnchantmentId { get; }
    internal int CandidateCount { get; }
}

internal sealed class WorldRelationBuffer<TRow> where TRow : struct
{
    private TRow[] _rows = new TRow[16];
    internal int Count { get; private set; }
    internal ref readonly TRow this[int index] => ref _rows[index];
    internal void Reset() => Count = 0;
    internal void Append(in TRow row)
    {
        if (Count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[Count++] = row;
    }
}

internal static class WorldScribeRelationDeriver
{
    internal static PublicationTable<TRow> Build<TRow>(
        WorldRelationBuffer<TRow> buffer,
        Comparison<TRow> comparison)
        where TRow : struct
    {
        if (buffer.Count == 0) return PublicationTable<TRow>.Empty;
        var rows = new TRow[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, comparison);
        return PublicationTable<TRow>.Create(rows, rows.Length);
    }
}

internal static class WorldScribeLookup
{
    internal static bool TryGetRecipe(
        PublicationTable<WorldScribeRecipe> recipes,
        Guid recipeId,
        out WorldScribeRecipe recipe)
    {
        for (var index = 0; index < recipes.Count; index++)
        {
            var row = recipes[index];
            if (row.RecipeId != recipeId) continue;
            recipe = row;
            return true;
        }
        recipe = default;
        return false;
    }

    internal static int CountWorkAtOrAbove(
        PublicationTable<WorldScribeWork> work,
        Guid recipeId,
        int level)
    {
        var count = 0;
        for (var index = 0; index < work.Count; index++)
        {
            var row = work[index];
            if (row.RecipeId == recipeId && row.Level >= level && !row.IsExpired) count++;
        }
        return count;
    }

    internal static int CountTargets(
        PublicationTable<WorldScrollTarget> targets,
        Guid consumableId,
        Guid enchantmentId)
    {
        var count = 0;
        for (var index = 0; index < targets.Count; index++)
        {
            var row = targets[index];
            if (row.ConsumableId == consumableId && row.EnchantmentId == enchantmentId) count++;
        }
        return count;
    }

    internal static bool TryGetTargetEvidence(
        PublicationTable<WorldScrollTargetEvidence> evidence,
        Guid consumableId,
        Guid enchantmentId,
        out int candidateCount)
    {
        for (var index = 0; index < evidence.Count; index++)
        {
            var row = evidence[index];
            if (row.ConsumableId != consumableId ||
                row.EnchantmentId != enchantmentId)
            {
                continue;
            }
            candidateCount = row.CandidateCount;
            return true;
        }
        candidateCount = 0;
        return false;
    }

    internal static int EnchantmentLevel(
        PublicationTable<WorldStructureEnchantment> enchantments,
        Guid structureId,
        Guid enchantmentId)
    {
        for (var index = 0; index < enchantments.Count; index++)
        {
            var row = enchantments[index];
            if (row.StructureId == structureId && row.EnchantmentId == enchantmentId)
                return row.Level;
        }
        return 0;
    }
}

/// <summary>
/// Captures the exact Scribe production and Scroll-target graph. It publishes identities and scalar
/// facts only; no Unity or native object crosses the collection boundary.
/// </summary>
internal sealed class WorldScribeRelationReader : IWorldCategoryReader
{
    private const string RequestTargetEffectScriptName = "RequestTargetEffectScript";
    private const string TargetStructureName = "Targeting.TargetStructure";
    private const string EnchantItemScriptName = "EnchantmentSO+EnchantItemScript";
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _recipeType;
    private readonly Type? _registryType;
    private readonly Type? _consumableType;
    private readonly Type? _structureType;
    private readonly Type? _instanceListType;
    private readonly Type? _instanceType;
    private readonly Type? _enchantmentInstanceType;
    private readonly string _unavailable;

    internal WorldScribeRelationReader(Func<string, Type?> resolve)
    {
        _recipeType = resolve("CraftingRecipeSO");
        _registryType = resolve("IdScriptableObject");
        _consumableType = resolve("ConsumableSO");
        _structureType = resolve("StructureSO");
        _instanceListType = resolve("CraftingInstanceListVariable");
        _instanceType = resolve("CraftingInstance");
        _enchantmentInstanceType = resolve("EnchantmentInstance");
        _unavailable =
            _recipeType is null || _registryType is null ||
            _consumableType is null || _structureType is null ||
            _instanceListType is null || _instanceType is null || _enchantmentInstanceType is null
                ? "one or more exact Scribe relation types were unavailable"
                : string.Empty;
    }

    public string Category => "scribe relations";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.ScribeRecipes.Reset();
        frame.ScribeQueues.Reset();
        frame.ScribeWork.Reset();
        frame.StructureEnchantments.Reset();
        frame.ScrollTargets.Reset();
        frame.ScrollTargetEvidence.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var sampled = 0;
            sampled += ReadRecipes(frame);
            sampled += ReadQueues(frame);
            sampled += ReadStructures(frame);
            sampled += ReadTargets(frame);
            return new WorldCategoryReport(
                Category, WorldCategoryOutcome.Collected, sampled, 0, string.Empty);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or TargetInvocationException or MemberAccessException)
        {
            return WorldCategoryReport.Missing(Category, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private int ReadRecipes(GameWorldCycleFrame frame)
    {
        var all = RequireEnumerable(_recipeType!, "All", AnyStatic);
        var count = 0;
        foreach (var recipe in all)
        {
            RequireExact(recipe, _recipeType!);
            var id = GuidOf(recipe!);
            var typeIds = ReferenceIds(recipe!, "craftingTypes");
            if (!typeIds.Contains(KnownEntities.ScribeCrafting.Uuid)) continue;
            var outputs = RecipeOutputs(recipe!);
            if (outputs.Count != 1)
                throw new InvalidOperationException(
                    $"Scribe recipe {id:D} did not have exactly one ConsumableGainEffect output.");
            frame.ScribeRecipes.Append(new WorldScribeRecipe(
                id,
                KnownEntities.ScribeCrafting.Uuid,
                outputs[0],
                Invoke<bool>(recipe!, "IsVisible"),
                Read<bool>(recipe!, "useQuantityAsLevel")));
            count++;
        }
        return count;
    }

    private int ReadQueues(GameWorldCycleFrame frame)
    {
        var registry = _registryType is null
            ? null
            : NativeAccessorBinder.StaticDictionary(_registryType, "RuntimeLookup");
        if (registry is null)
            throw new InvalidOperationException("The identity registry was unavailable.");

        var count = 0;
        foreach (var queueId in new[]
        {
            KnownEntities.ActiveScribeInstances.Uuid,
            KnownEntities.AutoScribeInstances.Uuid,
        })
        {
            if (registry[queueId] is not object queue) continue;
            RequireExact(queue, _instanceListType!);
            var values = RequireEnumerable(queue, "value");
            var maximum = Invoke<int>(queue, "GetMax");
            frame.ScribeQueues.Append(new WorldScribeQueue(
                queueId, Read<bool>(queue, "isAutoList"), CollectionCount(values), maximum));
            foreach (var work in values)
            {
                RequireExact(work, _instanceType!);
                frame.ScribeWork.Append(new WorldScribeWork(
                    queueId,
                    Invoke<Guid>(work!, "GetGuidReference"),
                    BigDoubleToLevel(InvokeObject(work!, "GetQuantity")),
                    Invoke<bool>(work!, "IsAuto"),
                    Invoke<bool>(work!, "IsExpired")));
                count++;
            }
            count++;
        }
        return count;
    }

    private int ReadStructures(GameWorldCycleFrame frame)
    {
        var count = 0;
        foreach (var structure in RequireEnumerable(_structureType!, "All", AnyStatic))
        {
            RequireExact(structure, _structureType!);
            var structureId = GuidOf(structure!);
            var table = ReadObject(structure!, "enchantTable");
            if (table is null) continue;
            foreach (var instance in RequireEnumerable(table, "enchantments"))
            {
                RequireExact(instance, _enchantmentInstanceType!);
                frame.StructureEnchantments.Append(new WorldStructureEnchantment(
                    structureId,
                    Invoke<Guid>(instance!, "GetGuidReference"),
                    BigDoubleToLevel(
                        TryInvokeObject(instance!, "GetLevel") ??
                        TryReadObject(instance!, "level") ??
                        0)));
                count++;
            }
        }
        return count;
    }

    private int ReadTargets(GameWorldCycleFrame frame)
    {
        var count = 0;
        foreach (var consumable in RequireEnumerable(_consumableType!, "All", AnyStatic))
        {
            RequireExact(consumable, _consumableType!);
            var relation = DiscoverTargetRelation(consumable!);
            if (relation.EnchantmentId == Guid.Empty) continue;
            var structures = relation.ContainingStructures ??
                RequireEnumerable(_structureType!, "All", AnyStatic);
            var candidates = 0;
            foreach (var structure in structures)
            {
                RequireExact(structure, _structureType!);
                if (!Invoke<bool>(structure!, "IsVisible")) continue;
                if (relation.Condition is not null &&
                    !Invoke<bool>(relation.Condition, "IsValid", _structureType!, structure!))
                    continue;
                frame.ScrollTargets.Append(new WorldScrollTarget(
                    GuidOf(consumable!), relation.EnchantmentId, GuidOf(structure!)));
                candidates++;
                count++;
            }
            frame.ScrollTargetEvidence.Append(new WorldScrollTargetEvidence(
                GuidOf(consumable!), relation.EnchantmentId, candidates));
            count++;
        }
        return count;
    }

    private TargetRelation DiscoverTargetRelation(object consumable)
    {
        var matchedEnchantmentId = Guid.Empty;
        var appliedEnchantmentId = Guid.Empty;
        object? condition = null;
        IEnumerable? containingStructures = null;
        var requestedReference = -1;
        var appliedReference = -1;
        var requestCount = 0;
        var enchantCount = 0;
        foreach (var block in OptionalEnumerable(consumable, "onUseEffects"))
        foreach (var script in OptionalEnumerable(block!, "effectScripts"))
        {
            if (script is null) continue;
            var type = script.GetType();
            if (string.Equals(
                    type.FullName,
                    RequestTargetEffectScriptName,
                    StringComparison.Ordinal))
            {
                requestCount++;
                var options = ReadObject(script, "targetOptions");
                var targeting = options is null
                    ? null
                    : InvokeObject(options, "GetTargeting");
                if (targeting is null ||
                    !string.Equals(
                        targeting.GetType().FullName,
                        TargetStructureName,
                        StringComparison.Ordinal))
                {
                    return default;
                }
                requestedReference = Convert.ToInt32(
                    InvokeObject(options!, "GetTargetRefType"));
                condition = ReadObject(targeting, "condition");
                var matcher = ReadObject(targeting, "enchantmentMatcher");
                var ids = matcher is null
                    ? new List<Guid>()
                    : ReferenceIds(matcher, "enchantments");
                if (ids.Count != 1) return default;
                matchedEnchantmentId = ids[0];
                var restricted = ReadObject(targeting, "containingStructures");
                if (restricted is not null)
                {
                    containingStructures =
                        InvokeObject(restricted, "ToList") as IEnumerable ??
                        throw new InvalidOperationException(
                            "TargetStructure.containingStructures did not expose ToList().");
                }
            }
            else if (string.Equals(
                         type.FullName,
                         EnchantItemScriptName,
                         StringComparison.Ordinal))
            {
                enchantCount++;
                var enchantment = ReadObject(script, "enchantment");
                var targetReference = ReadObject(script, "targetReference");
                if (enchantment is null || targetReference is null) return default;
                appliedEnchantmentId = GuidOf(enchantment);
                appliedReference = Convert.ToInt32(
                    ReadObject(targetReference, "refType"));
            }
        }
        return requestCount == 1 &&
               enchantCount == 1 &&
               requestedReference >= 0 &&
               requestedReference == appliedReference &&
               condition is not null &&
               matchedEnchantmentId != Guid.Empty &&
               matchedEnchantmentId == appliedEnchantmentId
            ? new TargetRelation(
                matchedEnchantmentId,
                condition,
                containingStructures)
            : default;
    }

    private List<Guid> RecipeOutputs(object recipe)
    {
        var outputs = new List<Guid>();
        foreach (var block in OptionalEnumerable(recipe, "completeEffects"))
        foreach (var script in OptionalEnumerable(block!, "effectScripts"))
        {
            if (script?.GetType().Name != "ConsumableGainEffect") continue;
            var output = ReadObject(script, "consumable");
            if (output is not null) outputs.Add(GuidOf(output));
        }
        return outputs;
    }

    private static List<Guid> ReferenceIds(object owner, string field)
    {
        var ids = new List<Guid>();
        foreach (var item in OptionalEnumerable(owner, field))
            if (item is not null) ids.Add(GuidOf(item));
        return ids;
    }

    private static IEnumerable RequireEnumerable(
        object owner,
        string name,
        BindingFlags flags = AnyInstance)
    {
        var value = owner is Type type
            ? type.GetField(name, flags)?.GetValue(null)
            : owner.GetType().GetField(name, flags)?.GetValue(owner);
        return value as IEnumerable ??
            throw new InvalidOperationException($"{owner.GetType().Name}.{name} was unavailable.");
    }

    private static IEnumerable OptionalEnumerable(object owner, string name) =>
        owner.GetType().GetField(name, AnyInstance)?.GetValue(owner) as IEnumerable ??
        Array.Empty<object>();

    private static object? ReadObject(object owner, string field) =>
        owner.GetType().GetField(field, AnyInstance)?.GetValue(owner);

    private static object? TryReadObject(object owner, string field) => ReadObject(owner, field);

    private static T Read<T>(object owner, string field) =>
        owner.GetType().GetField(field, AnyInstance)?.GetValue(owner) is T value
            ? value
            : throw new InvalidOperationException($"{owner.GetType().Name}.{field} changed type.");

    private static object InvokeObject(object owner, string method) =>
        TryInvokeObject(owner, method) ??
        throw new InvalidOperationException($"{owner.GetType().Name}.{method} returned null.");

    private static object? TryInvokeObject(object owner, string method) =>
        owner.GetType().GetMethod(method, AnyInstance, null, Type.EmptyTypes, null)?.Invoke(owner, null);

    private static T Invoke<T>(object owner, string method) =>
        owner.GetType().GetMethod(method, AnyInstance, null, Type.EmptyTypes, null)?.Invoke(owner, null)
            is T value
                ? value
                : throw new InvalidOperationException($"{owner.GetType().Name}.{method} changed contract.");

    private static T Invoke<T>(
        object owner,
        string method,
        Type parameterType,
        object argument) =>
        owner.GetType().GetMethod(method, AnyInstance, null, new[] { parameterType }, null)?
            .Invoke(owner, new[] { argument }) is T value
                ? value
                : throw new InvalidOperationException($"{owner.GetType().Name}.{method} changed overload.");

    private static Guid GuidOf(object owner) =>
        owner.GetType().GetMethod("GetGuid", AnyInstance, null, Type.EmptyTypes, null)?
            .Invoke(owner, null) is Guid value && value != Guid.Empty
                ? value
                : throw new InvalidOperationException($"{owner.GetType().Name} had no stable identity.");

    private static void RequireExact(object? value, Type type)
    {
        if (value is null || value.GetType() != type)
            throw new InvalidOperationException($"Expected exact {type.Name}.");
    }

    private static int CollectionCount(IEnumerable values)
    {
        if (values is ICollection collection) return collection.Count;
        var count = 0;
        foreach (var _ in values) count++;
        return count;
    }

    private static int BigDoubleToLevel(object value)
    {
        if (value is int integer) return integer;
        if (value is BigDouble big)
        {
            var number = big.ToDouble();
            return double.IsFinite(number) && number >= 0d && number <= int.MaxValue
                ? (int)Math.Floor(number)
                : 0;
        }
        return 0;
    }

    private readonly struct TargetRelation
    {
        internal TargetRelation(
            Guid enchantmentId,
            object? condition,
            IEnumerable? containingStructures)
        {
            EnchantmentId = enchantmentId;
            Condition = condition;
            ContainingStructures = containingStructures;
        }

        internal Guid EnchantmentId { get; }
        internal object? Condition { get; }
        internal IEnumerable? ContainingStructures { get; }
    }
}
