using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldScribeRecipe : IWorldEntity
{
    internal WorldScribeRecipe(
        Guid recipeId,
        Guid recipeTypeId,
        Guid outputConsumableId,
        bool visible,
        bool usesQuantityAsLevel)
    {
        RecipeId = recipeId;
        RecipeTypeId = recipeTypeId;
        OutputConsumableId = outputConsumableId;
        Visible = visible;
        UsesQuantityAsLevel = usesQuantityAsLevel;
    }

    public Guid EntityId => RecipeId;
    internal Guid RecipeId { get; }
    internal Guid RecipeTypeId { get; }
    internal Guid OutputConsumableId { get; }
    internal bool Visible { get; }
    internal bool UsesQuantityAsLevel { get; }
}

internal readonly struct WorldScribeQueue : IWorldEntity
{
    internal WorldScribeQueue(Guid queueId, bool isAutomatic, int used, int maximum)
    {
        QueueId = queueId;
        IsAutomatic = isAutomatic;
        Used = used;
        Maximum = maximum;
    }

    public Guid EntityId => QueueId;
    internal Guid QueueId { get; }
    internal bool IsAutomatic { get; }
    internal int Used { get; }
    internal int Maximum { get; }
}

internal readonly struct WorldScribeWork
{
    internal WorldScribeWork(
        Guid queueId,
        Guid recipeId,
        int level,
        bool isAutomatic,
        bool isExpired)
    {
        QueueId = queueId;
        RecipeId = recipeId;
        Level = level;
        IsAutomatic = isAutomatic;
        IsExpired = isExpired;
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
        StructureId = structureId;
        EnchantmentId = enchantmentId;
        Level = level;
    }

    internal Guid StructureId { get; }
    internal Guid EnchantmentId { get; }
    internal int Level { get; }
}

internal readonly struct WorldScrollTarget
{
    internal WorldScrollTarget(Guid consumableId, Guid enchantmentId, Guid structureId)
    {
        ConsumableId = consumableId;
        EnchantmentId = enchantmentId;
        StructureId = structureId;
    }

    internal Guid ConsumableId { get; }
    internal Guid EnchantmentId { get; }
    internal Guid StructureId { get; }
}

/// <summary>
/// Completeness marker for one accepted Scroll target graph. Zero candidates is a complete fact.
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
            if (recipes[index].RecipeId != recipeId) continue;
            recipe = recipes[index];
            return true;
        }
        recipe = default;
        return false;
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
            if (row.ConsumableId != consumableId || row.EnchantmentId != enchantmentId)
                continue;
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
/// Captures Scribe relationships through one constructor-bound exact schema. The warm path only
/// invokes retained metadata; member discovery cannot fail halfway through a collection.
/// </summary>
internal sealed class WorldScribeRelationReader : IWorldCategoryReader
{
    private readonly BindingSet? _native;
    private readonly string _unavailable;

    internal WorldScribeRelationReader(Func<string, Type?> resolve)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        if (BindingSet.TryCreate(resolve, out var native, out var reason))
        {
            _native = native;
            _unavailable = string.Empty;
        }
        else
        {
            _unavailable = reason;
        }
    }

    public string Category => "scribe relations";
    public bool IsAvailable => _native is not null;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.ScribeRecipes.Reset();
        frame.ScribeQueues.Reset();
        frame.ScribeWork.Reset();
        frame.StructureEnchantments.Reset();
        frame.ScrollTargets.Reset();
        frame.ScrollTargetEvidence.Reset();
        if (_native is not { } native)
            return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var sampled = ReadRecipes(native, frame);
            sampled += ReadQueues(native, frame);
            sampled += ReadEnchantments(native, frame);
            sampled += ReadTargets(native, frame);
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                skipped: 0,
                firstFailure: string.Empty);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return WorldCategoryReport.Missing(
                Category,
                ex.GetBaseException().Message);
        }
    }

    private static int ReadRecipes(BindingSet native, GameWorldCycleFrame frame)
    {
        var registry = Resolve(
            native,
            KnownEntities.ScribeCraftingRecipes.Uuid,
            native.RecipeListType);
        var recipes = RequireList(
            native.RecipeListValue.GetValue(registry),
            "ScribeCraftingRecipes.value");
        var sampled = 0;
        foreach (var value in recipes)
        {
            var recipe = RequireExact(value, native.RecipeType, "Scribe recipe");
            var recipeId = Invoke<Guid>(native.RecipeIdentity, recipe);
            var types = RequireEnumerable(
                native.RecipeTypes.GetValue(recipe),
                "CraftingRecipeSO.craftingTypes");
            var typeCount = 0;
            var typeId = Guid.Empty;
            foreach (var valueType in types)
            {
                var exactType = RequireExact(valueType, native.RecipeTypeType, "recipe type");
                typeCount++;
                typeId = Invoke<Guid>(native.RecipeTypeIdentity, exactType);
            }

            var outputCount = 0;
            var outputId = Guid.Empty;
            foreach (var blockValue in RequireEnumerable(
                         native.CompleteEffects.GetValue(recipe),
                         "CraftingRecipeSO.completeEffects"))
            {
                var block = RequireExact(blockValue, native.InstantBlockType, "complete effect block");
                foreach (var scriptValue in RequireEnumerable(
                             native.EffectScripts.GetValue(block),
                             "InstantEffectBlock.effectScripts"))
                {
                    if (scriptValue is null || !native.InstantScriptType.IsInstanceOfType(scriptValue))
                        throw new InvalidOperationException(
                            $"Scribe recipe {EntityUuidTranslator.Format(recipeId)} contained a " +
                            "non-IInstantEffectScript output.");
                    if (scriptValue.GetType() != native.ConsumableGainType) continue;
                    var output = RequireExact(
                        native.GainConsumable.GetValue(scriptValue),
                        native.ConsumableType,
                        "ConsumableGainEffect.consumable");
                    outputCount++;
                    outputId = Invoke<Guid>(native.ConsumableIdentity, output);
                }
            }
            if (typeCount != 1 || outputCount != 1)
                throw new InvalidOperationException(
                    $"Scribe recipe {EntityUuidTranslator.Format(recipeId)} had {typeCount} " +
                    "recipe types and " +
                    $"{outputCount} ConsumableGainEffect outputs; exactly one of each is required.");

            frame.ScribeRecipes.Append(new WorldScribeRecipe(
                recipeId,
                typeId,
                outputId,
                Invoke<bool>(native.RecipeVisible, recipe),
                Require<bool>(native.UseQuantityAsLevel.GetValue(recipe),
                    "CraftingRecipeSO.useQuantityAsLevel")));
            sampled++;
        }
        return sampled;
    }

    private static int ReadQueues(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        foreach (var queueId in new[]
        {
            KnownEntities.ActiveScribeInstances.Uuid,
            KnownEntities.AutoScribeInstances.Uuid,
        })
        {
            var queue = Resolve(native, queueId, native.InstanceListType);
            var values = RequireList(
                native.InstanceListValue.GetValue(queue),
                "CraftingInstance list value");
            frame.ScribeQueues.Append(new WorldScribeQueue(
                queueId,
                Require<bool>(native.AutoList.GetValue(queue),
                    "CraftingInstanceListVariable.isAutoList"),
                CountNonNull(values),
                Invoke<int>(native.ListMaximum, queue)));
            sampled++;
            foreach (var value in values)
            {
                if (value is null) continue;
                var instance = RequireExact(value, native.InstanceType, "CraftingInstance");
                frame.ScribeWork.Append(new WorldScribeWork(
                    queueId,
                    Invoke<Guid>(native.InstanceRecipe, instance),
                    Level(InvokeObject(native.InstanceQuantity, instance)),
                    Invoke<bool>(native.InstanceAutomatic, instance),
                    Invoke<bool>(native.InstanceExpired, instance)));
                sampled++;
            }
        }
        return sampled;
    }

    private static int ReadEnchantments(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        foreach (var value in RequireEnumerable(
                     native.StructureAll.GetValue(null),
                     "StructureSO.All"))
        {
            var structure = RequireExact(value, native.StructureType, "StructureSO");
            var structureId = Invoke<Guid>(native.StructureIdentity, structure);
            var table = RequireExact(
                native.EnchantTable.GetValue(structure),
                native.EnchantTableType,
                "EnchantmentSO.EnchantTable");
            foreach (var entryValue in RequireEnumerable(
                         native.Enchantments.GetValue(table),
                         "EnchantmentSO.EnchantTable.enchantments"))
            {
                var entry = RequireExact(
                    entryValue,
                    native.EnchantmentInstanceType,
                    "EnchantmentInstance");
                frame.StructureEnchantments.Append(new WorldStructureEnchantment(
                    structureId,
                    Invoke<Guid>(native.EnchantmentInstanceIdentity, entry),
                    Invoke<int>(native.EnchantmentLevel, entry)));
                sampled++;
            }
        }
        return sampled;
    }

    private static int ReadTargets(BindingSet native, GameWorldCycleFrame frame)
    {
        var sampled = 0;
        for (var index = 0; index < TargetRoles.Length; index++)
        {
            var role = TargetRoles[index];
            var consumable = Resolve(native, role.ScrollId, native.ConsumableType);
            var targeting = ResolveTargeting(native, consumable, role.EnchantmentId);
            var recipeType = Resolve(
                native,
                KnownEntities.ScribeCrafting.Uuid,
                native.RecipeTypeType);
            var level = Math.Max(
                1,
                Require<int>(
                    native.MaximumStartingLevel.GetValue(recipeType),
                    "CraftingRecipeTypeSO.maxStartingLevel"));
            var scaling = InvokeObject(
                native.ScalingBasic,
                target: null,
                new BigDouble(level, 0));
            if (scaling.GetType() != native.ScalingType)
                throw new InvalidOperationException("ScalingInfo.Basic(BigDouble) changed return type.");
            var candidates = RequireEnumerable(
                native.GetRandomList.Invoke(targeting, new[] { scaling }),
                "Targeting.TargetStructure.GetRandomList");
            var count = 0;
            foreach (var candidateValue in candidates)
            {
                var candidate = RequireExact(candidateValue, native.StructureType, "Scroll target");
                frame.ScrollTargets.Append(new WorldScrollTarget(
                    role.ScrollId,
                    role.EnchantmentId,
                    Invoke<Guid>(native.StructureIdentity, candidate)));
                count++;
                sampled++;
            }
            frame.ScrollTargetEvidence.Append(new WorldScrollTargetEvidence(
                role.ScrollId,
                role.EnchantmentId,
                count));
            sampled++;
        }
        return sampled;
    }

    private static object ResolveTargeting(
        BindingSet native,
        object consumable,
        Guid expectedEnchantment)
    {
        object? options = null;
        var requestCount = 0;
        var enchantCount = 0;
        var enchantment = Guid.Empty;
        foreach (var blockValue in RequireEnumerable(
                     native.OnUseEffects.GetValue(consumable),
                     "ConsumableSO.onUseEffects"))
        {
            var block = RequireExact(blockValue, native.InstantBlockType, "on-use effect block");
            foreach (var scriptValue in RequireEnumerable(
                         native.EffectScripts.GetValue(block),
                         "InstantEffectBlock.effectScripts"))
            {
                if (scriptValue is null || !native.InstantScriptType.IsInstanceOfType(scriptValue))
                    throw new InvalidOperationException(
                        "Scroll on-use effects contained a non-IInstantEffectScript value.");
                if (scriptValue.GetType() == native.RequestType)
                {
                    requestCount++;
                    options = native.TargetOptions.GetValue(scriptValue);
                }
                else if (scriptValue.GetType() == native.EnchantScriptType)
                {
                    enchantCount++;
                    var enchant = RequireExact(
                        native.EnchantScriptEnchantment.GetValue(scriptValue),
                        native.EnchantmentType,
                        "EnchantItemScript.enchantment");
                    enchantment = Invoke<Guid>(native.EnchantmentIdentity, enchant);
                }
            }
        }
        if (requestCount != 1 || enchantCount != 1 || enchantment != expectedEnchantment)
            throw new InvalidOperationException(
                $"Scroll {EntityUuidTranslator.Format(Invoke<Guid>(native.ConsumableIdentity, consumable))} " +
                $"had {requestCount} target " +
                $"requests, {enchantCount} enchant effects, and enchantment " +
                $"{EntityUuidTranslator.Format(enchantment)}; expected exactly one of each and " +
                $"{EntityUuidTranslator.Format(expectedEnchantment)}.");
        var exactOptions = RequireExact(options, native.OptionsType, "TargetSelectOptions");
        var targeting = InvokeObject(native.GetTargeting, exactOptions);
        return RequireExact(targeting, native.TargetStructureType, "TargetStructure");
    }

    private static object Resolve(BindingSet native, Guid id, Type exactType)
    {
        if (native.Registry.GetValue(null) is not IDictionary registry ||
            !registry.Contains(id))
            throw new InvalidOperationException(
                $"The identity registry did not contain {exactType.Name} " +
                $"{EntityUuidTranslator.Format(id)}.");
        return RequireExact(registry[id], exactType, exactType.Name);
    }

    private static int CountNonNull(IList values)
    {
        var count = 0;
        foreach (var value in values)
            if (value is not null) count++;
        return count;
    }

    private static int Level(object value)
    {
        if (value is int integer) return integer;
        if (value is BigDouble number)
        {
            var scalar = number.ToDouble();
            if (double.IsFinite(scalar) && scalar >= 0 && scalar <= int.MaxValue)
                return (int)Math.Floor(scalar);
        }
        throw new InvalidOperationException("A Scribe level was not a finite non-negative integer.");
    }

    private static T Require<T>(object? value, string contract) =>
        value is T typed
            ? typed
            : throw new InvalidOperationException(contract + " changed type.");

    private static object RequireExact(object? value, Type type, string contract) =>
        value is not null && value.GetType() == type
            ? value
            : throw new InvalidOperationException(contract + " was not the exact audited type.");

    private static IEnumerable RequireEnumerable(object? value, string contract) =>
        value as IEnumerable ??
        throw new InvalidOperationException(contract + " was not enumerable.");

    private static IList RequireList(object? value, string contract) =>
        value as IList ??
        throw new InvalidOperationException(contract + " was not a list.");

    private static object InvokeObject(MethodInfo method, object? target, params object[] arguments) =>
        method.Invoke(target, arguments) ??
        throw new InvalidOperationException(
            $"{method.DeclaringType?.Name}.{method.Name} returned null.");

    private static T Invoke<T>(MethodInfo method, object? target, params object[] arguments) =>
        method.Invoke(target, arguments) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} changed return type.");

    private static bool IsExpected(Exception exception) => exception is
        TargetInvocationException or
        ArgumentException or
        InvalidOperationException or
        InvalidCastException or
        OverflowException or
        TargetException or
        TargetParameterCountException or
        MemberAccessException or
        TypeLoadException;

    private readonly record struct TargetRole(Guid ScrollId, Guid EnchantmentId);

    private static readonly TargetRole[] TargetRoles =
    {
        new(KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
        new(KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
        new(KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
        new(KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
        new(KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
        new(KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
        new(KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
        new(KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
    };

    private sealed class BindingSet
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private BindingSet(
            Type recipeType,
            Type recipeListType,
            Type recipeTypeType,
            Type instanceListType,
            Type instanceType,
            Type consumableType,
            Type structureType,
            Type enchantTableType,
            Type enchantmentInstanceType,
            Type enchantmentType,
            Type scalingType,
            Type instantBlockType,
            Type instantScriptType,
            Type consumableGainType,
            Type requestType,
            Type optionsType,
            Type targetStructureType,
            Type enchantScriptType,
            FieldInfo registry,
            FieldInfo recipeListValue,
            FieldInfo instanceListValue,
            FieldInfo recipeTypes,
            FieldInfo completeEffects,
            FieldInfo useQuantityAsLevel,
            FieldInfo effectScripts,
            FieldInfo gainConsumable,
            FieldInfo autoList,
            FieldInfo structureAll,
            FieldInfo enchantTable,
            FieldInfo enchantments,
            FieldInfo maximumStartingLevel,
            FieldInfo onUseEffects,
            FieldInfo targetOptions,
            FieldInfo enchantScriptEnchantment,
            MethodInfo recipeIdentity,
            MethodInfo recipeTypeIdentity,
            MethodInfo consumableIdentity,
            MethodInfo structureIdentity,
            MethodInfo recipeVisible,
            MethodInfo listMaximum,
            MethodInfo instanceRecipe,
            MethodInfo instanceQuantity,
            MethodInfo instanceAutomatic,
            MethodInfo instanceExpired,
            MethodInfo enchantmentInstanceIdentity,
            MethodInfo enchantmentIdentity,
            MethodInfo enchantmentLevel,
            MethodInfo scalingBasic,
            MethodInfo getTargeting,
            MethodInfo getRandomList)
        {
            RecipeType = recipeType;
            RecipeListType = recipeListType;
            RecipeTypeType = recipeTypeType;
            InstanceListType = instanceListType;
            InstanceType = instanceType;
            ConsumableType = consumableType;
            StructureType = structureType;
            EnchantTableType = enchantTableType;
            EnchantmentInstanceType = enchantmentInstanceType;
            EnchantmentType = enchantmentType;
            ScalingType = scalingType;
            InstantBlockType = instantBlockType;
            InstantScriptType = instantScriptType;
            ConsumableGainType = consumableGainType;
            RequestType = requestType;
            OptionsType = optionsType;
            TargetStructureType = targetStructureType;
            EnchantScriptType = enchantScriptType;
            Registry = registry;
            RecipeListValue = recipeListValue;
            InstanceListValue = instanceListValue;
            RecipeTypes = recipeTypes;
            CompleteEffects = completeEffects;
            UseQuantityAsLevel = useQuantityAsLevel;
            EffectScripts = effectScripts;
            GainConsumable = gainConsumable;
            AutoList = autoList;
            StructureAll = structureAll;
            EnchantTable = enchantTable;
            Enchantments = enchantments;
            MaximumStartingLevel = maximumStartingLevel;
            OnUseEffects = onUseEffects;
            TargetOptions = targetOptions;
            EnchantScriptEnchantment = enchantScriptEnchantment;
            RecipeIdentity = recipeIdentity;
            RecipeTypeIdentity = recipeTypeIdentity;
            ConsumableIdentity = consumableIdentity;
            StructureIdentity = structureIdentity;
            RecipeVisible = recipeVisible;
            ListMaximum = listMaximum;
            InstanceRecipe = instanceRecipe;
            InstanceQuantity = instanceQuantity;
            InstanceAutomatic = instanceAutomatic;
            InstanceExpired = instanceExpired;
            EnchantmentInstanceIdentity = enchantmentInstanceIdentity;
            EnchantmentIdentity = enchantmentIdentity;
            EnchantmentLevel = enchantmentLevel;
            ScalingBasic = scalingBasic;
            GetTargeting = getTargeting;
            GetRandomList = getRandomList;
        }

        internal Type RecipeType { get; }
        internal Type RecipeListType { get; }
        internal Type RecipeTypeType { get; }
        internal Type InstanceListType { get; }
        internal Type InstanceType { get; }
        internal Type ConsumableType { get; }
        internal Type StructureType { get; }
        internal Type EnchantTableType { get; }
        internal Type EnchantmentInstanceType { get; }
        internal Type EnchantmentType { get; }
        internal Type ScalingType { get; }
        internal Type InstantBlockType { get; }
        internal Type InstantScriptType { get; }
        internal Type ConsumableGainType { get; }
        internal Type RequestType { get; }
        internal Type OptionsType { get; }
        internal Type TargetStructureType { get; }
        internal Type EnchantScriptType { get; }
        internal FieldInfo Registry { get; }
        internal FieldInfo RecipeListValue { get; }
        internal FieldInfo InstanceListValue { get; }
        internal FieldInfo RecipeTypes { get; }
        internal FieldInfo CompleteEffects { get; }
        internal FieldInfo UseQuantityAsLevel { get; }
        internal FieldInfo EffectScripts { get; }
        internal FieldInfo GainConsumable { get; }
        internal FieldInfo AutoList { get; }
        internal FieldInfo StructureAll { get; }
        internal FieldInfo EnchantTable { get; }
        internal FieldInfo Enchantments { get; }
        internal FieldInfo MaximumStartingLevel { get; }
        internal FieldInfo OnUseEffects { get; }
        internal FieldInfo TargetOptions { get; }
        internal FieldInfo EnchantScriptEnchantment { get; }
        internal MethodInfo RecipeIdentity { get; }
        internal MethodInfo RecipeTypeIdentity { get; }
        internal MethodInfo ConsumableIdentity { get; }
        internal MethodInfo StructureIdentity { get; }
        internal MethodInfo RecipeVisible { get; }
        internal MethodInfo ListMaximum { get; }
        internal MethodInfo InstanceRecipe { get; }
        internal MethodInfo InstanceQuantity { get; }
        internal MethodInfo InstanceAutomatic { get; }
        internal MethodInfo InstanceExpired { get; }
        internal MethodInfo EnchantmentInstanceIdentity { get; }
        internal MethodInfo EnchantmentIdentity { get; }
        internal MethodInfo EnchantmentLevel { get; }
        internal MethodInfo ScalingBasic { get; }
        internal MethodInfo GetTargeting { get; }
        internal MethodInfo GetRandomList { get; }

        internal static bool TryCreate(
            Func<string, Type?> resolve,
            out BindingSet? bindings,
            out string reason)
        {
            bindings = null;
            try
            {
                var id = Type(resolve, "IdScriptableObject");
                var recipe = Type(resolve, "CraftingRecipeSO");
                var recipeList = Type(resolve, "CraftingRecipeListVariable");
                var recipeType = Type(resolve, "CraftingRecipeTypeSO");
                var instanceList = Type(resolve, "CraftingInstanceListVariable");
                var instance = Type(resolve, "CraftingInstance");
                var consumable = Type(resolve, "ConsumableSO");
                var structure = Type(resolve, "StructureSO");
                var enchantTable = Type(resolve, "EnchantmentSO+EnchantTable");
                var enchantInstance = Type(resolve, "EnchantmentInstance");
                var enchantment = Type(resolve, "EnchantmentSO");
                var scaling = Type(resolve, "ScalingInfo");
                var bigDouble = Type(resolve, "BigDouble");
                var block = Type(resolve, "InstantEffectBlock");
                var script = Type(resolve, "IInstantEffectScript");
                var gain = Type(resolve, "ConsumableSO+ConsumableGainEffect");
                var request = Type(resolve, "RequestTargetEffectScript");
                var options = Type(resolve, "Targeting.TargetSelectOptions");
                var selection = Type(resolve, "Targeting.BaseTargetSelection");
                var target = Type(resolve, "Targeting.TargetStructure");
                var targetable = Type(resolve, "Targeting.ITargetable");
                var enchantScript = Type(resolve, "EnchantmentSO+EnchantItemScript");

                bindings = new BindingSet(
                    recipe,
                    recipeList,
                    recipeType,
                    instanceList,
                    instance,
                    consumable,
                    structure,
                    enchantTable,
                    enchantInstance,
                    enchantment,
                    scaling,
                    block,
                    script,
                    gain,
                    request,
                    options,
                    target,
                    enchantScript,
                    Field(id, "RuntimeLookup", Static, typeof(Dictionary<Guid, object>), allowDictionary: true),
                    GenericListValue(recipeList, recipe),
                    GenericListValue(instanceList, instance),
                    CollectionField(recipe, "craftingTypes", recipeType),
                    CollectionField(recipe, "completeEffects", block),
                    Field(recipe, "useQuantityAsLevel", Instance, typeof(bool)),
                    CollectionField(block, "effectScripts", script),
                    Field(gain, "consumable", Instance, consumable),
                    Field(instanceList, "isAutoList", Instance, typeof(bool)),
                    CollectionField(structure, "All", structure, Static),
                    Field(structure, "enchantTable", Instance, enchantTable),
                    CollectionField(enchantTable, "enchantments", enchantInstance),
                    Field(recipeType, "maxStartingLevel", Instance, typeof(int)),
                    CollectionField(consumable, "onUseEffects", block),
                    Field(request, "targetOptions", Instance, options),
                    Field(enchantScript, "enchantment", Instance, enchantment),
                    MethodFromHierarchy(recipe, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(recipeType, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(consumable, "GetGuid", typeof(Guid)),
                    MethodFromHierarchy(structure, "GetGuid", typeof(Guid)),
                    Method(recipe, "IsVisible", typeof(bool), Instance),
                    MethodFromHierarchy(instanceList, "GetMax", typeof(int)),
                    MethodFromHierarchy(instance, "GetGuidReference", typeof(Guid)),
                    Method(instance, "GetQuantity", bigDouble, Instance),
                    Method(instance, "IsAuto", typeof(bool), Instance),
                    Method(instance, "IsExpired", typeof(bool), Instance),
                    MethodFromHierarchy(enchantInstance, "GetGuidReference", typeof(Guid)),
                    MethodFromHierarchy(enchantment, "GetGuid", typeof(Guid)),
                    Method(enchantInstance, "GetLevel", typeof(int), Instance),
                    Method(scaling, "Basic", scaling, Static, bigDouble),
                    Method(options, "GetTargeting", selection, Instance),
                    Method(
                        target,
                        "GetRandomList",
                        typeof(List<>).MakeGenericType(targetable),
                        Instance,
                        scaling));
                reason = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
            {
                reason = "The exact Scribe relationship binding set is unavailable: " + ex.Message;
                return false;
            }
        }

        private static Type Type(Func<string, Type?> resolve, string name) =>
            resolve(name) ?? throw new InvalidOperationException(name + " was unavailable.");

        private static FieldInfo GenericListValue(Type listType, Type elementType)
        {
            for (var current = listType; current is not null; current = current.BaseType)
            {
                var field = current.GetField("value", Instance | BindingFlags.DeclaredOnly);
                if (field is not null &&
                    field.FieldType == typeof(List<>).MakeGenericType(elementType))
                    return field;
            }
            throw new InvalidOperationException(
                $"{listType.Name}.value : List<{elementType.Name}> was unavailable.");
        }

        private static FieldInfo CollectionField(
            Type type,
            string name,
            Type element,
            BindingFlags flags = Instance)
        {
            var field = type.GetField(name, flags);
            if (field is null || CollectionElement(field.FieldType) != element ||
                field.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} collection of {element.Name} was unavailable.");
            return field;
        }

        private static FieldInfo Field(
            Type type,
            string name,
            BindingFlags flags,
            Type expected,
            bool allowDictionary = false)
        {
            var field = type.GetField(name, flags);
            var typeMatches = allowDictionary
                ? field?.FieldType.IsGenericType == true &&
                  field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                  field.FieldType.GetGenericArguments()[0] == typeof(Guid)
                : field?.FieldType == expected;
            if (field is null || !typeMatches ||
                field.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} : {expected.Name} was unavailable.");
            return field;
        }

        private static MethodInfo MethodFromHierarchy(
            Type type,
            string name,
            Type returnType,
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
                if (method?.ReturnType == returnType && !method.IsStatic) return method;
            }
            throw new InvalidOperationException(
                $"{type.Name}.{name}({string.Join(",", Array.ConvertAll(parameters, p => p.Name))}) " +
                $": {returnType.Name} was unavailable.");
        }

        private static MethodInfo Method(
            Type type,
            string name,
            Type returnType,
            BindingFlags flags,
            params Type[] parameters)
        {
            var method = type.GetMethod(name, flags, null, parameters, null);
            if (method is null || method.ReturnType != returnType ||
                method.IsStatic != flags.HasFlag(BindingFlags.Static))
                throw new InvalidOperationException(
                    $"{type.Name}.{name} : {returnType.Name} was unavailable.");
            return method;
        }

        private static Type? CollectionElement(Type type)
        {
            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
                return type.GetGenericArguments()[0];
            foreach (var candidate in type.GetInterfaces())
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return candidate.GetGenericArguments()[0];
            return null;
        }
    }
}
