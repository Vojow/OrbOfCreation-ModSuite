using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common;

public enum AlchemyGameplayDomain
{
    Unknown = 0,
    OrdinaryAlchemy = 1,
    ScholarConcept = 2,
}

public enum AlchemyDomainClassifierStatus
{
    Uninitialized = 0,
    Retryable = 1,
    Ready = 2,
    Blocked = 3,
}

[Flags]
public enum AlchemyDomainEvidence
{
    None = 0,
    ExactNativeRecipeType = 1 << 0,
    StableRecipeUuid = 1 << 1,
    ConceptRegistrySnapshot = 1 << 2,
    ConceptRegistryMember = 1 << 3,
    ExactNativeAlchemyType = 1 << 4,
    StableAlchemyTypeUuid = 1 << 5,
    KnownOrdinaryAlchemyType = 1 << 6,
    KnownScholarConceptType = 1 << 7,
}

public sealed class AlchemyDomainClassification
{
    internal AlchemyDomainClassification(
        AlchemyGameplayDomain domain,
        Guid? recipeUuid,
        IReadOnlyList<Guid> alchemyTypeUuids,
        AlchemyDomainEvidence evidence,
        int lifecycleGeneration,
        string reason)
    {
        Domain = domain;
        RecipeUuid = recipeUuid;
        AlchemyTypeUuids = alchemyTypeUuids;
        Evidence = evidence;
        LifecycleGeneration = lifecycleGeneration;
        Reason = reason;
    }

    public AlchemyGameplayDomain Domain { get; }
    public Guid? RecipeUuid { get; }
    public IReadOnlyList<Guid> AlchemyTypeUuids { get; }
    public AlchemyDomainEvidence Evidence { get; }
    public int LifecycleGeneration { get; }
    public string Reason { get; }
    public bool IsKnown => Domain != AlchemyGameplayDomain.Unknown;
}

/// <summary>
/// Classifies native alchemy recipes and types without using asset names.
/// Initialize once per game lifecycle, classify from the cached snapshot, and invalidate on
/// scene, save-load, reset, or NG+ transitions.
/// </summary>
public sealed class AlchemyGameplayDomainClassifier : IDisposable
{
    public static readonly Guid ConceptRecipesUuid = new Guid("c8ff8e01-c042-49c2-86a2-e374f82c280c");

    public static readonly Guid AlchemyTypeUuid = new Guid("f9c93e42-e9e8-4fe3-a1f3-5aec5430b5c2");
    public static readonly Guid BrewingTypeUuid = new Guid("d2947f69-d989-465d-8159-204285ed57be");
    public static readonly Guid DismantleTypeUuid = new Guid("7b89d22c-75ae-4945-9356-833382c9a167");
    public static readonly Guid EnchantmentTypeUuid = new Guid("2ffcbbc4-49a7-45db-b3ae-4a3c57362255");
    public static readonly Guid RefinementTypeUuid = new Guid("32b6b099-19f2-4470-b47b-6c2a8b0388e1");
    public static readonly Guid TransmutationTypeUuid = new Guid("b42c6192-7d9b-40d0-aa40-3d46a9348e52");

    public static readonly Guid ReductiveConceptTypeUuid = new Guid("47b787b9-d4cd-43c8-a7e3-63a1e4e0ae94");
    public static readonly Guid ReflectiveConceptTypeUuid = new Guid("8f258dcc-c39a-4d64-b915-4239e746c49d");
    public static readonly Guid ConceptualizationTypeUuid = new Guid("69842862-dfce-4a9e-a73b-f757c72e49dc");

    private static readonly HashSet<Guid> OrdinaryTypeUuids = new HashSet<Guid>
    {
        AlchemyTypeUuid,
        BrewingTypeUuid,
        DismantleTypeUuid,
        EnchantmentTypeUuid,
        RefinementTypeUuid,
        TransmutationTypeUuid,
    };

    private static readonly HashSet<Guid> ConceptTypeUuids = new HashSet<Guid>
    {
        ReductiveConceptTypeUuid,
        ReflectiveConceptTypeUuid,
        ConceptualizationTypeUuid,
    };

    private readonly Dictionary<Guid, CachedRecipe> _recipes = new Dictionary<Guid, CachedRecipe>();
    private Type? _recipeType;
    private Type? _alchemyType;
    private FieldInfo? _alchemyTypesField;
    private MethodInfo? _getGuid;
    private string _statusReason = "classifier has not been initialized for this lifecycle";
    private int _lifecycleGeneration = 1;

    public AlchemyDomainClassifierStatus Status { get; private set; } = AlchemyDomainClassifierStatus.Uninitialized;
    public string StatusReason => _statusReason;
    public int LifecycleGeneration => _lifecycleGeneration;
    public int CachedRecipeCount => _recipes.Count;

    public bool TryInitialize(out string reason)
    {
        if (Status == AlchemyDomainClassifierStatus.Ready)
        {
            reason = string.Empty;
            return true;
        }

        if (Status == AlchemyDomainClassifierStatus.Blocked)
        {
            reason = _statusReason;
            return false;
        }

        try
        {
            var idType = FindLoadedType("IdScriptableObject");
            _recipeType = FindLoadedType("AlchemyRecipeSO");
            _alchemyType = FindLoadedType("AlchemyTypeSO");
            var recipeListType = FindLoadedType("AlchemyRecipeListVariable");
            if (idType is null || _recipeType is null || _alchemyType is null || recipeListType is null)
                return Retry("native alchemy classifier types are not loaded yet", out reason);

            var lookupField = FindField(idType, "RuntimeLookup", BindingFlags.Static);
            _getGuid = FindNoArgumentGuidMethod(idType, "GetGuid");
            _alchemyTypesField = FindField(_recipeType, "alchemyTypes", BindingFlags.Instance);
            var valuesField = FindField(recipeListType, "value", BindingFlags.Instance);
            if (lookupField is null || _getGuid is null || _alchemyTypesField is null || valuesField is null)
                return Block("native alchemy classifier metadata does not match the audited contract", out reason);

            if (lookupField.GetValue(null) is not IDictionary lookup)
                return Retry("IdScriptableObject.RuntimeLookup is not ready", out reason);
            if (!lookup.Contains(ConceptRecipesUuid))
                return Retry("ConceptRecipes is not registered yet", out reason);

            var registry = lookup[ConceptRecipesUuid];
            if (registry is null || registry.GetType() != recipeListType)
                return Block("ConceptRecipes UUID/type mismatch", out reason);
            if (!TryReadStableUuid(registry, out var registryUuid) || registryUuid != ConceptRecipesUuid)
                return Block("ConceptRecipes stable UUID does not match its registry key", out reason);
            if (valuesField.GetValue(registry) is not IEnumerable recipes)
                return Block("ConceptRecipes contents are unavailable", out reason);

            var snapshot = new Dictionary<Guid, CachedRecipe>();
            foreach (var recipe in recipes)
            {
                if (recipe is null || recipe.GetType() != _recipeType)
                    return Block("ConceptRecipes contains an unexpected native recipe type", out reason);
                if (!TryReadStableUuid(recipe, out var recipeUuid))
                    return Block("ConceptRecipes contains a recipe without a stable UUID", out reason);
                if (snapshot.ContainsKey(recipeUuid))
                    return Block("ConceptRecipes contains a duplicate recipe UUID", out reason);

                var classification = ClassifyRecipeEvidence(recipe, recipeUuid, isConceptRegistryMember: true);
                if (classification.Domain != AlchemyGameplayDomain.ScholarConcept)
                    return Block("ConceptRecipes contains a recipe without verified Scholar type evidence", out reason);
                snapshot.Add(recipeUuid, new CachedRecipe(recipe, classification));
            }

            if (snapshot.Count == 0)
                return Retry("ConceptRecipes is empty for the current lifecycle", out reason);

            _recipes.Clear();
            foreach (var entry in snapshot) _recipes.Add(entry.Key, entry.Value);
            Status = AlchemyDomainClassifierStatus.Ready;
            _statusReason = string.Empty;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedReflectionFailure(ex))
        {
            return Block("native alchemy classifier initialization failed: " + ex.GetBaseException().Message, out reason);
        }
    }

    public AlchemyDomainClassification ClassifyRecipe(object? recipe)
    {
        if (Status != AlchemyDomainClassifierStatus.Ready)
            return Unknown(null, AlchemyDomainEvidence.None, _statusReason);
        if (recipe is null || recipe.GetType() != _recipeType)
            return Unknown(null, AlchemyDomainEvidence.None, "value is not the exact native AlchemyRecipeSO type");
        if (!TryReadStableUuid(recipe, out var recipeUuid))
            return Unknown(null, AlchemyDomainEvidence.ExactNativeRecipeType, "recipe stable UUID is unavailable");

        if (_recipes.TryGetValue(recipeUuid, out var cached))
        {
            if (!ReferenceEquals(recipe, cached.NativeRecipe))
            {
                return Unknown(
                    recipeUuid,
                    AlchemyDomainEvidence.ExactNativeRecipeType |
                    AlchemyDomainEvidence.StableRecipeUuid |
                    AlchemyDomainEvidence.ConceptRegistrySnapshot,
                    "recipe UUID resolves to a different lifecycle-scoped native reference");
            }
            return cached.Classification;
        }

        var classification = ClassifyRecipeEvidence(recipe, recipeUuid, isConceptRegistryMember: false);
        _recipes.Add(recipeUuid, new CachedRecipe(recipe, classification));
        return classification;
    }

    public AlchemyDomainClassification ClassifyType(object? alchemyType)
    {
        if (Status != AlchemyDomainClassifierStatus.Ready)
            return Unknown(null, AlchemyDomainEvidence.None, _statusReason);
        if (alchemyType is null || alchemyType.GetType() != _alchemyType)
            return Unknown(null, AlchemyDomainEvidence.None, "value is not the exact native AlchemyTypeSO type");
        if (!TryReadStableUuid(alchemyType, out var typeUuid))
            return Unknown(null, AlchemyDomainEvidence.ExactNativeAlchemyType, "alchemy type stable UUID is unavailable");

        var evidence = AlchemyDomainEvidence.ExactNativeAlchemyType | AlchemyDomainEvidence.StableAlchemyTypeUuid;
        if (ConceptTypeUuids.Contains(typeUuid))
        {
            return Result(
                AlchemyGameplayDomain.ScholarConcept,
                null,
                new[] { typeUuid },
                evidence | AlchemyDomainEvidence.KnownScholarConceptType,
                "stable UUID is one of the three audited Scholar concept types");
        }
        if (OrdinaryTypeUuids.Contains(typeUuid))
        {
            return Result(
                AlchemyGameplayDomain.OrdinaryAlchemy,
                null,
                new[] { typeUuid },
                evidence | AlchemyDomainEvidence.KnownOrdinaryAlchemyType,
                "stable UUID is one of the six audited ordinary alchemy types");
        }
        return Result(AlchemyGameplayDomain.Unknown, null, new[] { typeUuid }, evidence, "alchemy type UUID is not in the audited domain mapping");
    }

    public void InvalidateLifecycle()
    {
        _recipes.Clear();
        _recipeType = null;
        _alchemyType = null;
        _alchemyTypesField = null;
        _getGuid = null;
        _lifecycleGeneration++;
        Status = AlchemyDomainClassifierStatus.Uninitialized;
        _statusReason = "classifier has not been initialized for this lifecycle";
    }

    public void Dispose() => InvalidateLifecycle();

    private AlchemyDomainClassification ClassifyRecipeEvidence(object recipe, Guid recipeUuid, bool isConceptRegistryMember)
    {
        var evidence = AlchemyDomainEvidence.ExactNativeRecipeType |
            AlchemyDomainEvidence.StableRecipeUuid |
            AlchemyDomainEvidence.ConceptRegistrySnapshot;
        if (isConceptRegistryMember) evidence |= AlchemyDomainEvidence.ConceptRegistryMember;

        if (_alchemyTypesField!.GetValue(recipe) is not IEnumerable types)
            return Unknown(recipeUuid, evidence, "recipe alchemy type evidence is unavailable");

        var typeUuids = new List<Guid>();
        var hasOrdinaryType = false;
        var hasConceptType = false;
        foreach (var type in types)
        {
            if (type is null || type.GetType() != _alchemyType || !TryReadStableUuid(type, out var typeUuid))
                return Unknown(recipeUuid, evidence, "recipe contains an invalid alchemy type reference");
            typeUuids.Add(typeUuid);
            evidence |= AlchemyDomainEvidence.ExactNativeAlchemyType | AlchemyDomainEvidence.StableAlchemyTypeUuid;
            if (OrdinaryTypeUuids.Contains(typeUuid)) hasOrdinaryType = true;
            if (ConceptTypeUuids.Contains(typeUuid)) hasConceptType = true;
        }

        if (hasOrdinaryType) evidence |= AlchemyDomainEvidence.KnownOrdinaryAlchemyType;
        if (hasConceptType) evidence |= AlchemyDomainEvidence.KnownScholarConceptType;
        var frozenTypeUuids = typeUuids.AsReadOnly();

        if (hasOrdinaryType && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe carries contradictory ordinary-alchemy and Scholar-concept type evidence");
        }

        if (isConceptRegistryMember && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.ScholarConcept,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe is an exact ConceptRecipes member with an audited Scholar type UUID");
        }
        if (!isConceptRegistryMember && hasConceptType)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe has a Scholar type UUID but is absent from the ConceptRecipes snapshot");
        }
        if (isConceptRegistryMember)
        {
            return Result(
                AlchemyGameplayDomain.Unknown,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "ConceptRecipes membership is missing an audited Scholar type UUID");
        }
        if (hasOrdinaryType)
        {
            return Result(
                AlchemyGameplayDomain.OrdinaryAlchemy,
                recipeUuid,
                frozenTypeUuids,
                evidence,
                "recipe is outside ConceptRecipes and has an audited ordinary alchemy type UUID");
        }
        return Result(
            AlchemyGameplayDomain.Unknown,
            recipeUuid,
            frozenTypeUuids,
            evidence,
            "recipe has no audited ordinary alchemy or Scholar concept type UUID");
    }

    private bool TryReadStableUuid(object instance, out Guid uuid)
    {
        try
        {
            var value = _getGuid!.Invoke(instance, Array.Empty<object>());
            if (value is Guid result && result != Guid.Empty)
            {
                uuid = result;
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedReflectionFailure(ex))
        {
        }
        uuid = Guid.Empty;
        return false;
    }

    private AlchemyDomainClassification Unknown(Guid? recipeUuid, AlchemyDomainEvidence evidence, string reason) =>
        Result(AlchemyGameplayDomain.Unknown, recipeUuid, Array.Empty<Guid>(), evidence, reason);

    private AlchemyDomainClassification Result(
        AlchemyGameplayDomain domain,
        Guid? recipeUuid,
        IReadOnlyList<Guid> typeUuids,
        AlchemyDomainEvidence evidence,
        string reason) =>
        new AlchemyDomainClassification(domain, recipeUuid, typeUuids, evidence, _lifecycleGeneration, reason);

    private bool Retry(string message, out string reason)
    {
        _recipes.Clear();
        Status = AlchemyDomainClassifierStatus.Retryable;
        _statusReason = message;
        reason = message;
        return false;
    }

    private bool Block(string message, out string reason)
    {
        _recipes.Clear();
        Status = AlchemyDomainClassifierStatus.Blocked;
        _statusReason = message;
        reason = message;
        return false;
    }

    private static Type? FindLoadedType(string typeName)
    {
        var gameType = Type.GetType(typeName + ", Assembly-CSharp", throwOnError: false);
        if (gameType is not null) return gameType;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            var candidate = assemblies[index].GetType(typeName, throwOnError: false, ignoreCase: false);
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, string name, BindingFlags scope)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        return null;
    }

    private static MethodInfo? FindNoArgumentGuidMethod(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method is not null && method.ReturnType == typeof(Guid)) return method;
        }
        return null;
    }

    private static bool IsExpectedReflectionFailure(Exception ex) =>
        ex is ArgumentException ||
        ex is InvalidOperationException ||
        ex is TargetInvocationException ||
        ex is TargetException ||
        ex is MethodAccessException ||
        ex is FieldAccessException;

    private sealed class CachedRecipe
    {
        public CachedRecipe(object nativeRecipe, AlchemyDomainClassification classification)
        {
            NativeRecipe = nativeRecipe;
            Classification = classification;
        }

        public object NativeRecipe { get; }
        public AlchemyDomainClassification Classification { get; }
    }
}
