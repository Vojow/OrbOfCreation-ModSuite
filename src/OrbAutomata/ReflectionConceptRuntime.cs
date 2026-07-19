using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class NativeConceptCandidate
{
    public NativeConceptCandidate(
        string uuid,
        string displayName,
        object recipe,
        string slotTypeUuid,
        int masteryLevel,
        double masteryProgress,
        int maximumQuantity,
        object? instance,
        int quantity,
        int queuedQuantity)
    {
        Uuid = uuid;
        DisplayName = displayName;
        Recipe = recipe;
        SlotTypeUuid = slotTypeUuid;
        MasteryLevel = masteryLevel;
        MasteryProgress = masteryProgress;
        MaximumQuantity = maximumQuantity;
        Instance = instance;
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
    }

    public string Uuid { get; }
    public string DisplayName { get; }
    public object Recipe { get; }
    public string SlotTypeUuid { get; }
    public int MasteryLevel { get; }
    public double MasteryProgress { get; }
    public int MaximumQuantity { get; }
    public object? Instance { get; }
    public int Quantity { get; }
    public int QueuedQuantity { get; }
    public bool IsSettled => Quantity == QueuedQuantity;
}

internal sealed class ReflectionConceptRuntime : IDisposable, INativeMutationOutcomeSource
{
    internal static readonly string ActiveConceptsUuid = KnownEntities.ActiveConcepts.Uuid.ToString("D");

    private readonly AlchemyGameplayDomainClassifier _domainClassifier;
    private readonly TypedRegistryResolver _registryResolver;
    private readonly List<object> _recipes = new();
    private readonly Dictionary<object, string> _recipeUuids = new(ReferenceEqualityComparer.Instance);
    private object? _activeConcepts;
    private TypedRegistryResolution? _activeConceptsResolution;
    private TypedRegistryResolution? _conceptRecipesResolution;
    private Type? _recipeType;
    private Type? _instanceType;
    private FieldInfo? _activeValuesField;
    private FieldInfo? _recipeDrainField;
    private FieldInfo? _instanceQuantityField;
    private FieldInfo? _instanceQueuedQuantityField;
    private FieldInfo? _instanceDrainField;
    private MethodInfo? _canAddInstance;
    private MethodInfo? _addInstances;
    private MethodInfo? _removeInstances;
    private string? _blockedReason;
    private NativeMutationEvidence<int>? _lastMutationEvidence;
    private NativeMutationCallOutcome _lastNativeMutationOutcome;
    private int _activeConceptCount;

    public string? BlockedReason => _blockedReason;
    public bool IsReady =>
        _activeConcepts is not null &&
        _activeConceptsResolution is not null &&
        _conceptRecipesResolution is not null &&
        _registryResolver.IsCurrent(_activeConceptsResolution) &&
        _registryResolver.IsCurrent(_conceptRecipesResolution) &&
        _blockedReason is null;
    public int ScopedRecipeCount => _recipes.Count;
    public int ActiveConceptCount => _activeConceptCount;
    public NativeMutationCallOutcome LastNativeMutationOutcome => _lastNativeMutationOutcome;

    public bool TryResolveInvalidationEntityId(object nativeRecipe, out string entityId)
    {
        if (nativeRecipe is not null &&
            _recipeUuids.TryGetValue(nativeRecipe, out var uuid) &&
            !string.IsNullOrWhiteSpace(uuid))
        {
            entityId = uuid;
            return true;
        }

        entityId = string.Empty;
        return false;
    }

    public ReflectionConceptRuntime(
        AlchemyGameplayDomainClassifier domainClassifier,
        TypedRegistryResolver? registryResolver = null)
    {
        _domainClassifier = domainClassifier ?? throw new ArgumentNullException(nameof(domainClassifier));
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public bool TryInitialize(out string reason)
    {
        if (IsReady)
        {
            reason = string.Empty;
            return true;
        }
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }
        try
        {
            if (!_domainClassifier.TryInitialize(out var classifierReason))
            {
                return _domainClassifier.Status == AlchemyDomainClassifierStatus.Blocked
                    ? Fail($"Auto Concept domain classifier blocked: {classifierReason}", out reason)
                    : Retry($"Auto Concept domain classifier is not ready: {classifierReason}", out reason);
            }

            _recipeType = ReflectionUtil.FindLoadedType("AlchemyRecipeSO");
            _instanceType = ReflectionUtil.FindLoadedType("AlchemyInstance");
            var activeType = ReflectionUtil.FindLoadedType(KnownEntities.ActiveConcepts.ManagedTypeName);
            var recipeListType = ReflectionUtil.FindLoadedType(KnownEntities.ConceptRecipes.ManagedTypeName);
            if (_recipeType is null || _instanceType is null || activeType is null || recipeListType is null)
                return Retry("native concept types are not registered yet", out reason);

            var activeId = KnownEntities.ActiveConcepts.Uuid;
            var recipesId = AlchemyGameplayDomainClassifier.ConceptRecipesUuid;
            var activeResolution = _registryResolver.Resolve(activeId, activeType);
            if (!activeResolution.IsResolved)
                return HandleRegistryFailure(KnownEntities.ActiveConcepts.DiagnosticName, activeResolution, out reason);
            var recipesResolution = _registryResolver.Resolve(recipesId, recipeListType);
            if (!recipesResolution.IsResolved)
                return HandleRegistryFailure(KnownEntities.ConceptRecipes.DiagnosticName, recipesResolution, out reason);
            var active = activeResolution.Value!;
            var recipes = recipesResolution.Value!;
            _activeConceptsResolution = activeResolution;
            _conceptRecipesResolution = recipesResolution;

            _activeValuesField = FindField(activeType, "value", isStatic: false);
            var recipeValuesField = FindField(recipeListType, "value", isStatic: false);
            _recipeDrainField = FindField(_recipeType, "drainCost", isStatic: false);
            _instanceQuantityField = FindField(_instanceType, "quantity", isStatic: false);
            _instanceQueuedQuantityField = FindField(_instanceType, "queuedQuantity", isStatic: false);
            _instanceDrainField = FindField(_instanceType, "resourceDrain", isStatic: false);
            _canAddInstance = FindMethod(activeType, "CanAddInstance", _recipeType);
            _addInstances = FindMethod(activeType, "AddAlchemyInstances", _recipeType, typeof(int));
            _removeInstances = FindMethod(activeType, "RemoveAlchemyInstances", _recipeType, typeof(int));
            if (_activeValuesField is null || recipeValuesField is null || _recipeDrainField is null ||
                _instanceQuantityField is null || _instanceQueuedQuantityField is null || _instanceDrainField is null ||
                _canAddInstance is null || _addInstances is null || _removeInstances is null)
                return Fail("native Active Concepts accessors are unavailable", out reason);

            if (recipeValuesField.GetValue(recipes) is not IEnumerable scopedRecipes)
                return Fail("ConceptRecipes runtime contents are unavailable", out reason);
            _recipes.Clear();
            _recipeUuids.Clear();
            foreach (var recipe in scopedRecipes)
            {
                var classification = _domainClassifier.ClassifyRecipe(recipe);
                if (classification.Domain != AlchemyGameplayDomain.ScholarConcept ||
                    !classification.IsMutationGrade ||
                    classification.RecipeUuid is null)
                {
                    return Fail(
                        $"ConceptRecipes entry failed shared domain classification. " +
                        $"RecipeUuid={classification.RecipeUuid?.ToString() ?? "unavailable"}, " +
                        $"Evidence={classification.Evidence}, Level={classification.Assessment.Level}, " +
                        $"Sources={classification.Assessment.Sources}, Reason={classification.Reason}",
                        out reason);
                }
                var uuid = classification.RecipeUuid.Value.ToString();
                _recipes.Add(recipe);
                _recipeUuids.Add(recipe, uuid);
            }
            if (_recipes.Count == 0) return Fail("ConceptRecipes runtime list is empty", out reason);
            _activeConcepts = active;
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
        {
            return Fail($"concept contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    public IReadOnlyList<NativeConceptCandidate> ReadCandidates(
        ISet<string> allowed,
        ISet<string> blocked,
        out string reason)
    {
        if (!TryInitialize(out reason)) return Array.Empty<NativeConceptCandidate>();
        var result = new List<NativeConceptCandidate>(_recipes.Count);
        try
        {
            var active = ReadActiveByRecipe();
            _activeConceptCount = active.Count;
            foreach (var recipe in _recipes)
            {
                var uuid = _recipeUuids[recipe];
                if (blocked.Contains(uuid) || allowed.Count > 0 && !allowed.Contains(uuid)) continue;
                if (ReflectionUtil.InvokeNoArgs(recipe, "IsDiscovered") is not true) continue;
                var masteryLevel = ReadInt(recipe, "GetExperienceLevel", "masteryLevel");
                var xp = ReadBig(recipe, "GetExperience");
                var required = ReadBig(recipe, "GetRequiredExperience");
                var progress = required.IsZero ? 1.0 : xp.DivideApprox(required);
                var maximum = Math.Max(0, ReadInt(recipe, "GetMaxUsageSlots", "maxUsageSlots"));
                var coreType = ReflectionUtil.InvokeNoArgs(recipe, "GetCoreType");
                var slotTypeUuid = coreType is null ? string.Empty : ReflectionUtil.ReadStableId(coreType) ?? string.Empty;
                active.TryGetValue(recipe, out var instance);
                var quantity = instance is null ? 0 : Convert.ToInt32(_instanceQuantityField!.GetValue(instance) ?? 0);
                var queued = instance is null ? 0 : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(instance) ?? 0);
                result.Add(new NativeConceptCandidate(
                    uuid,
                    ReflectionUtil.ReadDisplayName(recipe) ?? uuid,
                    recipe,
                    slotTypeUuid,
                    masteryLevel,
                    progress,
                    maximum,
                    instance,
                    Math.Max(0, quantity),
                    Math.Max(0, queued)));
            }
            reason = string.Empty;
            return result;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
        {
            reason = ex.GetBaseException().Message;
            return Array.Empty<NativeConceptCandidate>();
        }
    }

    public bool CanAdd(NativeConceptCandidate candidate)
    {
        try
        {
            return IsCurrentRecipe(candidate) &&
                _canAddInstance!.Invoke(_activeConcepts, new[] { candidate.Recipe }) is true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return false;
        }
    }

    public bool TryFindSafeTarget(
        NativeConceptCandidate candidate,
        int desiredTarget,
        float rateReservePercent,
        float minimumResourcePercent,
        out int safeTarget,
        out string reason)
    {
        safeTarget = candidate.Quantity;
        reason = "no resource-safe quantity was found";
        desiredTarget = Math.Min(desiredTarget, candidate.MaximumQuantity);
        if (desiredTarget <= candidate.Quantity)
        {
            reason = "native mastery quantity limit reached";
            return false;
        }
        var delta = desiredTarget - candidate.Quantity;
        while (delta > 0)
        {
            var target = candidate.Quantity + delta;
            if (TryValidateProjectedDrain(candidate, target, rateReservePercent, minimumResourcePercent, out reason))
            {
                safeTarget = target;
                return true;
            }
            delta /= 2;
        }
        return false;
    }

    public bool TryAdd(NativeConceptCandidate candidate, int delta, out string reason)
    {
        _lastNativeMutationOutcome = default;
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }

        if (delta <= 0 || !IsCurrentRecipe(candidate) || !CanAdd(candidate))
        {
            reason = "candidate identity, compatible slot, or quantity changed";
            return false;
        }
        var maximum = ReadInt(candidate.Recipe, "GetMaxUsageSlots", "maxUsageSlots");
        var current = FindActiveInstance(candidate.Recipe);
        var quantity = current is null ? 0 : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(current) ?? 0);
        if (quantity < 0 || quantity + delta > maximum)
        {
            reason = "native mastery quantity limit changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept add",
            $"queued quantity exact delta +{delta}",
            () => _addInstances!.Invoke(_activeConcepts, new object[] { candidate.Recipe, delta }),
            (before, after) => after == before + delta,
            out reason);
    }

    public bool TryRemoveOwned(NativeConceptCandidate candidate, int delta, out string reason)
    {
        return TryRemove(candidate, delta, requireExactQuantity: false, "owned concept quantity", out reason);
    }

    public bool TryRemoveForRotation(NativeConceptCandidate candidate, int expectedQuantity, out string reason)
    {
        return TryRemove(candidate, expectedQuantity, requireExactQuantity: true, "rotation quantity", out reason);
    }

    private bool TryRemove(
        NativeConceptCandidate candidate,
        int delta,
        bool requireExactQuantity,
        string quantityDescription,
        out string reason)
    {
        _lastNativeMutationOutcome = default;
        if (_blockedReason is not null)
        {
            reason = _blockedReason;
            return false;
        }

        var current = FindActiveInstance(candidate.Recipe);
        if (delta <= 0 || current is null || !IsCurrentRecipe(candidate))
        {
            reason = $"{quantityDescription} is no longer active";
            return false;
        }
        var quantity = Convert.ToInt32(_instanceQuantityField!.GetValue(current) ?? 0);
        var queuedQuantity = Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(current) ?? 0);
        if (queuedQuantity < delta || requireExactQuantity && (quantity != delta || queuedQuantity != delta))
        {
            reason = $"native {quantityDescription} changed";
            return false;
        }
        return ExecuteQuantityMutation(
            candidate,
            "Auto Concept remove",
            $"queued quantity exact delta -{delta}",
            () => _removeInstances!.Invoke(_activeConcepts, new object[] { candidate.Recipe, delta }),
            (before, after) => after == before - delta,
            out reason);
    }

    private bool ExecuteQuantityMutation(
        NativeConceptCandidate candidate,
        string feature,
        string expectedChange,
        Action execute,
        Func<int, int, bool> verify,
        out string reason)
    {
        var evidence = NativeMutationVerifier.Execute(
            feature,
            candidate.Uuid,
            expectedChange,
            () => CaptureQueuedQuantity(candidate.Recipe),
            execute,
            verify);
        _lastMutationEvidence = evidence;
        _lastNativeMutationOutcome = NativeMutationCallOutcome.FromEvidence(evidence);
        if (evidence.IsVerified)
        {
            reason = string.Empty;
            return true;
        }

        reason = evidence.Format();
        return evidence.MutationWasAttempted
            ? Fail($"native Concept mutation blocked until the next lifecycle: {reason}", out reason)
            : false;
    }

    private int CaptureQueuedQuantity(object recipe)
    {
        var instance = FindActiveInstance(recipe);
        return instance is null
            ? 0
            : Convert.ToInt32(_instanceQueuedQuantityField!.GetValue(instance) ?? 0);
    }

    public bool IsDrainSafe(NativeConceptCandidate candidate, float minimumDrainRatio)
    {
        if (candidate.Instance is null) return true;
        try
        {
            var drain = _instanceDrainField!.GetValue(candidate.Instance);
            if (drain is null) return false;
            if (!BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(drain, "GetRatio"), out var ratio)) return false;
            if (ratio.CompareTo(new BigAmount(minimumDrainRatio, 0)) < 0) return false;
            var currentDrain = ReflectionUtil.InvokeNoArgs(drain, "GetCurrentDrain");
            if (currentDrain is null || ReflectionUtil.InvokeNoArgs(currentDrain, "GetEntries") is not IList entries)
                return false;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var resource = entry is null ? null : ReflectionUtil.ReadMember(entry, "resource");
                var amount = entry is null ? null : ReflectionUtil.InvokeNoArgs(entry, "GetValue");
                if (resource is null || !BigAmount.TryRead(amount, out _) ||
                    ReflectionUtil.InvokeNoArgs(resource, "IsAtZero") is true) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return false;
        }
    }

    public void InvalidateLifecycle()
    {
        _domainClassifier.InvalidateLifecycle();
        _activeConcepts = null;
        _activeConceptsResolution = null;
        _conceptRecipesResolution = null;
        _recipes.Clear();
        _recipeUuids.Clear();
        _activeConceptCount = 0;
        _blockedReason = null;
        _lastMutationEvidence = null;
        _lastNativeMutationOutcome = default;
    }

    public void Dispose() => InvalidateLifecycle();

    private bool TryValidateProjectedDrain(
        NativeConceptCandidate candidate,
        int targetQuantity,
        float rateReservePercent,
        float minimumResourcePercent,
        out string reason)
    {
        try
        {
            var temporary = Activator.CreateInstance(
                _instanceType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[] { candidate.Recipe },
                culture: null);
            if (temporary is null) return FailProjection("native prospective instance could not be created", out reason);
            _instanceQuantityField!.SetValue(temporary, targetQuantity);
            var multiplier = ReflectionUtil.InvokeNoArgs(temporary, "GetDrainCostMod");
            var percentMultiplier = multiplier is null ? null : ReflectionUtil.InvokeNoArgs(multiplier, "AsPercent");
            var baseDrain = _recipeDrainField!.GetValue(candidate.Recipe);
            var prospective = baseDrain is null || percentMultiplier is null
                ? null
                : InvokeCompatible(baseDrain, "Multiply", percentMultiplier);
            if (prospective is null) return FailProjection("native prospective drain vector is unavailable", out reason);

            object? incremental = prospective;
            if (candidate.Instance is not null)
            {
                var drain = _instanceDrainField!.GetValue(candidate.Instance);
                var current = drain is null ? null : ReflectionUtil.InvokeNoArgs(drain, "GetCurrentDrain");
                if (current is null) return FailProjection("current native drain vector is unavailable", out reason);
                incremental = InvokeCompatible(prospective, "Subtract", current);
            }
            if (incremental is null) return FailProjection("incremental native drain vector is unavailable", out reason);
            if (!TryReadCostEntries(incremental, out var entries))
                return FailProjection("incremental native drain vector is only partially decodable", out reason);
            if (entries.Count == 0)
            {
                reason = "no positive incremental drain";
                return true;
            }
            foreach (var entry in entries)
            {
                if (entry.Amount.IsZero || entry.Amount.IsNegative) continue;
                var zeroState = ReflectionUtil.InvokeNoArgs(entry.Resource, "IsAtZero");
                if (!AutoConceptResourcePolicy.TryAcceptPositiveDrain(zeroState, out var zeroReason))
                    return FailProjection($"{ResourceName(entry.Resource)} {zeroReason}", out reason);
                var trueIncrement = InvokeCompatible(entry.Resource, "GetTrueSpend", entry.NativeAmount);
                if (!BigAmount.TryRead(trueIncrement, out var adjustedIncrement) || adjustedIncrement.IsNegative)
                    return FailProjection("resource quality conversion failed", out reason);
                if (!BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetTrueRate"), out var currentRate))
                    return FailProjection("resource true rate is unavailable", out reason);
                if (!BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetModdedDrain"), out var currentDrain))
                    return FailProjection("resource drain rate is unavailable", out reason);
                var grossRate = currentRate.Add(currentDrain);
                var reserve = grossRate.IsNegative
                    ? default
                    : grossRate.Multiply(Math.Clamp(rateReservePercent, 0.0f, 100.0f) / 100.0);
                if (currentRate.Subtract(adjustedIncrement).CompareTo(reserve) < 0)
                    return FailProjection($"{ResourceName(entry.Resource)} would fall below the configured rate reserve", out reason);

                if (ReflectionUtil.InvokeNoArgs(entry.Resource, "HasMaxQuantity") is true &&
                    BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetQuantity"), out var quantity) &&
                    BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(entry.Resource, "GetTrueSoftCap"), out var capacity) &&
                    !capacity.IsZero && quantity.DivideApprox(capacity) * 100.0 < minimumResourcePercent)
                    return FailProjection($"{ResourceName(entry.Resource)} is below the configured quantity floor", out reason);
            }
            reason = "projected native drain is safe";
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException || ex is MissingMethodException || ex is MemberAccessException || ex is OverflowException)
        {
            return FailProjection(ex.GetBaseException().Message, out reason);
        }
    }

    private Dictionary<object, object> ReadActiveByRecipe()
    {
        var result = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        if (_activeValuesField!.GetValue(_activeConcepts) is not IEnumerable values) return result;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != _instanceType) continue;
            var recipe = ReflectionUtil.ReadMember(instance, "reference");
            if (recipe is not null) result[recipe] = instance;
        }
        return result;
    }

    private object? FindActiveInstance(object recipe)
    {
        return ReadActiveByRecipe().TryGetValue(recipe, out var instance) ? instance : null;
    }

    private bool IsCurrentRecipe(NativeConceptCandidate candidate)
    {
        if (!IsReady) return false;
        if (candidate.Recipe.GetType() != _recipeType) return false;
        var classification = _domainClassifier.ClassifyRecipe(candidate.Recipe);
        if (classification.Domain != AlchemyGameplayDomain.ScholarConcept ||
            !classification.IsMutationGrade ||
            classification.RecipeUuid is null ||
            !string.Equals(classification.RecipeUuid.Value.ToString(), candidate.Uuid, StringComparison.Ordinal) ||
            !_recipeUuids.TryGetValue(candidate.Recipe, out var snapshotUuid) ||
            !string.Equals(snapshotUuid, candidate.Uuid, StringComparison.Ordinal)) return false;
        for (var index = 0; index < _recipes.Count; index++)
            if (ReferenceEquals(_recipes[index], candidate.Recipe)) return true;
        return false;
    }

    private static bool TryReadCostEntries(object? costList, out List<NativeCostEntry> result)
    {
        result = new List<NativeCostEntry>();
        if (costList is null || ReflectionUtil.InvokeNoArgs(costList, "GetEntries") is not IList entries) return false;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null) return false;
            var resource = ReflectionUtil.ReadMember(entry, "resource");
            var nativeAmount = ReflectionUtil.InvokeNoArgs(entry, "GetValue");
            if (resource is null || nativeAmount is null || !BigAmount.TryRead(nativeAmount, out var amount)) return false;
            result.Add(new NativeCostEntry(resource, nativeAmount, amount));
        }
        return true;
    }

    private static object? InvokeCompatible(object instance, string name, object argument)
    {
        foreach (var method in instance.GetType().GetMethods(ReflectionUtil.InstanceFlags))
        {
            var parameters = method.GetParameters();
            if (method.Name == name && parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
                return method.Invoke(instance, new[] { argument });
        }
        return null;
    }

    private static int ReadInt(object instance, string methodName, string fieldName)
    {
        var value = ReflectionUtil.InvokeNoArgs(instance, methodName) ?? FindField(instance.GetType(), fieldName, false)?.GetValue(instance);
        return Convert.ToInt32(value ?? 0);
    }

    private static BigAmount ReadBig(object instance, string methodName)
    {
        return BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(instance, methodName), out var value) ? value : default;
    }

    private static FieldInfo? FindField(Type type, string name, bool isStatic)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, ReflectionUtil.InstanceFlags | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field is not null && field.IsStatic == isStatic) return field;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                ReflectionUtil.InstanceFlags | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (method is not null) return method;
        }
        return null;
    }

    private bool HandleRegistryFailure(
        string label,
        TypedRegistryResolution resolution,
        out string reason)
    {
        var message = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable
            ? Retry(message, out reason)
            : Fail(message, out reason);
    }

    private bool Fail(string reason, out string output)
    {
        _blockedReason = reason;
        output = reason;
        return false;
    }

    private static bool FailProjection(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private static bool Retry(string reason, out string output)
    {
        output = reason;
        return false;
    }

    private static string ResourceName(object resource) =>
        ReflectionUtil.ReadDisplayName(resource) ?? ReflectionUtil.ReadStableId(resource) ?? "resource";

    private readonly struct NativeCostEntry
    {
        public NativeCostEntry(object resource, object nativeAmount, BigAmount amount)
        {
            Resource = resource;
            NativeAmount = nativeAmount;
            Amount = amount;
        }
        public object Resource { get; }
        public object NativeAmount { get; }
        public BigAmount Amount { get; }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
