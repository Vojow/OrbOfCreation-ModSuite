using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class ReflectionSpellLevelRuntime : IDisposable, INativeMutationOutcomeSource
{
    internal static readonly string UnlockLevelAllSpellsUuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D");

    private readonly TypedRegistryResolver _registryResolver;
    private Type? _recipeType;
    private object? _manager;
    private object? _availableRecipes;
    private object? _levelAllUpgrade;
    private TypedRegistryResolution? _levelAllResolution;
    private FieldInfo? _recipeValuesField;
    private FieldInfo? _levelingPrerequisitesField;
    private FieldInfo? _masteryLevelField;
    private MethodInfo? _prerequisitesCheck;
    private MethodInfo? _isDiscovered;
    private MethodInfo? _isReady;
    private MethodInfo? _getLevelCost;
    private MethodInfo? _costHasEnough;
    private MethodInfo? _costPerform;
    private MethodInfo? _purchaseLevel;
    private MethodInfo? _getUpgradePurchaseLevel;
    private MethodInfo? _tryLevelAll;
    private string? _blockedReason;
    private object? _lastMutationEvidence;
    private NativeMutationCallOutcome _lastNativeMutationOutcome;

    public string? BlockedReason => _blockedReason;
    public NativeMutationCallOutcome LastNativeMutationOutcome => _lastNativeMutationOutcome;
    public bool IsReady =>
        _manager is not null &&
        _availableRecipes is not null &&
        _levelAllResolution is not null &&
        _registryResolver.IsCurrent(_levelAllResolution) &&
        _blockedReason is null;

    public ReflectionSpellLevelRuntime(TypedRegistryResolver? registryResolver = null)
    {
        _registryResolver = registryResolver ?? TypedRegistryResolver.Shared;
    }

    public AutoSpellLevelSnapshot ReadSnapshot(out string reason)
    {
        if (!TryInitialize(out reason)) return new AutoSpellLevelSnapshot(AutoSpellLevelCapability.Locked, null);
        try
        {
            var capability = ReadPurchaseLevel(_levelAllUpgrade!) > 0
                ? AutoSpellLevelCapability.All
                : AutoSpellLevelCapability.Locked;
            NativeSpellLevelCandidate? candidate = null;
            foreach (var recipe in ReadRecipes())
            {
                if (recipe is null || recipe.GetType() != _recipeType)
                    return BlockSnapshot("available spell recipes contain an unexpected native type", out reason);
                if (_isDiscovered!.Invoke(recipe, Array.Empty<object>()) is not true) continue;
                var prerequisites = _levelingPrerequisitesField!.GetValue(recipe);
                if (prerequisites is null || _prerequisitesCheck!.Invoke(prerequisites, Array.Empty<object>()) is not true)
                    continue;
                if (capability == AutoSpellLevelCapability.Locked) capability = AutoSpellLevelCapability.Single;
                if (_isReady!.Invoke(recipe, Array.Empty<object>()) is not true || !HasAffordableCost(recipe)) continue;
                var current = CreateCandidate(recipe);
                if (candidate is null || Compare(current, candidate) < 0) candidate = current;
            }
            reason = string.Empty;
            return new AutoSpellLevelSnapshot(capability, candidate);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            reason = $"spell-level snapshot failed: {ex.GetBaseException().Message}";
            return new AutoSpellLevelSnapshot(AutoSpellLevelCapability.Locked, null);
        }
    }

    public bool TryLevelSingle(NativeSpellLevelCandidate candidate, out string reason)
    {
        _lastNativeMutationOutcome = default;
        if (!TryInitialize(out reason)) return false;
        try
        {
            if (!ContainsExactRecipe(candidate)) return Reject("spell-level candidate changed before mutation", out reason);
            var recipe = candidate.Recipe;
            if (_isDiscovered!.Invoke(recipe, Array.Empty<object>()) is not true)
                return Reject("spell is no longer discovered", out reason);
            var prerequisites = _levelingPrerequisitesField!.GetValue(recipe);
            if (prerequisites is null || _prerequisitesCheck!.Invoke(prerequisites, Array.Empty<object>()) is not true)
                return Reject("spell leveling is not unlocked", out reason);
            if (_isReady!.Invoke(recipe, Array.Empty<object>()) is not true)
                return Reject("spell no longer has a ready mastery level", out reason);
            var cost = _getLevelCost!.Invoke(recipe, Array.Empty<object>());
            if (cost is null || _costHasEnough!.Invoke(cost, Array.Empty<object>()) is not true)
                return Reject("spell-level cost is no longer affordable", out reason);

            var nativeCallsAttempted = 0;
            var evidence = NativeMutationVerifier.Execute(
                "Auto Spell Level single",
                candidate.Uuid,
                "mastery level exact delta +1",
                () => ReadMasteryLevel(recipe),
                () =>
                {
                    nativeCallsAttempted++;
                    _costPerform!.Invoke(cost, Array.Empty<object>());
                    nativeCallsAttempted++;
                    _purchaseLevel!.Invoke(recipe, Array.Empty<object>());
                },
                (before, after) => after == before + 1);
            return CompleteMutation(evidence, nativeCallsAttempted, out reason);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            reason = $"spell-level mutation failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    public bool TryLevelAll(out string reason)
    {
        _lastNativeMutationOutcome = default;
        var snapshot = ReadSnapshot(out reason);
        if (!string.IsNullOrWhiteSpace(reason) || snapshot.Capability != AutoSpellLevelCapability.All || snapshot.Candidate is null)
            return false;
        try
        {
            var identity = snapshot.Candidate.Uuid;
            var nativeCallsAttempted = 0;
            var evidence = NativeMutationVerifier.Execute(
                "Auto Spell Level all",
                identity,
                "total mastery level positive delta",
                ReadTotalMasteryLevels,
                () =>
                {
                    nativeCallsAttempted++;
                    _tryLevelAll!.Invoke(_manager, Array.Empty<object>());
                },
                (before, after) => after > before);
            return CompleteMutation(evidence, nativeCallsAttempted, out reason);
        }
        catch (Exception ex) when (IsReflectionFailure(ex))
        {
            return Block($"native level-all action failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    public void InvalidateLifecycle()
    {
        _recipeType = null;
        _manager = null;
        _availableRecipes = null;
        _levelAllUpgrade = null;
        _levelAllResolution = null;
        _recipeValuesField = null;
        _levelingPrerequisitesField = null;
        _masteryLevelField = null;
        _prerequisitesCheck = null;
        _isDiscovered = null;
        _isReady = null;
        _getLevelCost = null;
        _costHasEnough = null;
        _costPerform = null;
        _purchaseLevel = null;
        _getUpgradePurchaseLevel = null;
        _tryLevelAll = null;
        _blockedReason = null;
        _lastMutationEvidence = null;
        _lastNativeMutationOutcome = default;
    }

    public void Dispose() => InvalidateLifecycle();

    private bool CompleteMutation<TState>(
        NativeMutationEvidence<TState> evidence,
        int nativeCallsAttempted,
        out string reason)
    {
        _lastMutationEvidence = evidence;
        _lastNativeMutationOutcome = evidence.MutationWasAttempted && nativeCallsAttempted > 0
            ? new NativeMutationCallOutcome(
                nativeCallsAttempted,
                1,
                evidence.IsVerified ? 1 : 0)
            : default;
        if (evidence.IsVerified)
        {
            reason = string.Empty;
            return true;
        }

        reason = evidence.Format();
        return evidence.MutationWasAttempted
            ? Block($"native spell-level mutation blocked until the next lifecycle: {reason}", out reason)
            : false;
    }

    private bool TryInitialize(out string reason)
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
            var managerType = ReflectionUtil.FindLoadedType("SpellManager");
            _recipeType = ReflectionUtil.FindLoadedType("SpellRecipeSO");
            var upgradeType = ReflectionUtil.FindLoadedType(KnownEntities.UnlockLevelAllSpells.ManagedTypeName);
            if (managerType is null || _recipeType is null || upgradeType is null)
                return Retry("native spell-level types are not registered yet", out reason);

            var upgradeId = KnownEntities.UnlockLevelAllSpells.Uuid;
            var upgradeResolution = _registryResolver.Resolve(upgradeId, upgradeType);
            if (!upgradeResolution.IsResolved)
                return HandleRegistryFailure(KnownEntities.UnlockLevelAllSpells.DiagnosticName, upgradeResolution, out reason);
            _levelAllUpgrade = upgradeResolution.Value;
            _levelAllResolution = upgradeResolution;

            _manager = FindField(managerType, "instance", true)?.GetValue(null);
            if (_manager is null || _manager.GetType() != managerType)
                return Retry("SpellManager is not ready", out reason);
            var availableField = FindField(managerType, "availableSpellRecipes", false);
            _availableRecipes = availableField?.GetValue(_manager);
            if (_availableRecipes is null) return Retry("available spell recipes are not ready", out reason);
            _recipeValuesField = FindField(_availableRecipes.GetType(), "value", false);
            _levelingPrerequisitesField = FindField(_recipeType, "levelingPrerequisites", false);
            _masteryLevelField = FindField(_recipeType, "masteryLevel", false);
            _isDiscovered = FindMethod(_recipeType, "IsDiscovered");
            _isReady = FindMethod(_recipeType, "IsReadyToLevelMastery");
            _getLevelCost = FindMethod(_recipeType, "GetLevelCost");
            _purchaseLevel = FindMethod(_recipeType, "PurchaseLevel");
            _getUpgradePurchaseLevel = FindMethod(upgradeType, "GetPurchaseLevel");
            _tryLevelAll = FindMethod(managerType, "TryLevelAllSpells");
            if (_recipeValuesField is null || _levelingPrerequisitesField is null || _masteryLevelField is null ||
                _isDiscovered is null || _isReady is null || _getLevelCost is null || _purchaseLevel is null ||
                _getUpgradePurchaseLevel is null || _tryLevelAll is null)
                return Block("native spell-level accessors are unavailable", out reason);

            var prerequisitesType = _levelingPrerequisitesField.FieldType;
            var costType = _getLevelCost.ReturnType;
            _prerequisitesCheck = FindMethod(prerequisitesType, "Check");
            _costHasEnough = FindMethod(costType, "HasEnough");
            _costPerform = FindMethod(costType, "PerformCost");
            if (_prerequisitesCheck is null || _costHasEnough is null || _costPerform is null)
                return Block("native spell prerequisite or cost accessors are unavailable", out reason);

            _ = ReadRecipes();
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsReflectionFailure(ex) || ex is FormatException)
        {
            return Block($"spell-level contract initialization failed: {ex.GetBaseException().Message}", out reason);
        }
    }

    private IEnumerable ReadRecipes()
    {
        if (_recipeValuesField!.GetValue(_availableRecipes) is not IEnumerable recipes)
            throw new InvalidOperationException("available spell recipe contents are unavailable");
        return recipes;
    }

    private bool ContainsExactRecipe(NativeSpellLevelCandidate candidate)
    {
        foreach (var recipe in ReadRecipes())
            if (ReferenceEquals(recipe, candidate.Recipe) &&
                string.Equals(ReflectionUtil.ReadStableId(recipe), candidate.Uuid, StringComparison.Ordinal)) return true;
        return false;
    }

    private NativeSpellLevelCandidate CreateCandidate(object recipe) =>
        new(
            ReflectionUtil.ReadStableId(recipe) ?? throw new InvalidOperationException("spell UUID is unavailable"),
            ReflectionUtil.ReadDisplayName(recipe) ?? "Spell",
            recipe,
            ReadMasteryLevel(recipe));

    private bool HasAffordableCost(object recipe)
    {
        var cost = _getLevelCost!.Invoke(recipe, Array.Empty<object>());
        return cost is not null && _costHasEnough!.Invoke(cost, Array.Empty<object>()) is true;
    }

    private int ReadPurchaseLevel(object upgrade) =>
        Convert.ToInt32(_getUpgradePurchaseLevel!.Invoke(upgrade, Array.Empty<object>()) ?? 0);

    private int ReadMasteryLevel(object recipe) =>
        Convert.ToInt32(_masteryLevelField!.GetValue(recipe) ?? 0);

    private long ReadTotalMasteryLevels()
    {
        long total = 0;
        foreach (var recipe in ReadRecipes())
            if (recipe is not null && recipe.GetType() == _recipeType) total += Math.Max(0, ReadMasteryLevel(recipe));
        return total;
    }

    private static int Compare(NativeSpellLevelCandidate left, NativeSpellLevelCandidate right)
    {
        var level = left.MasteryLevel.CompareTo(right.MasteryLevel);
        return level != 0 ? level : StringComparer.Ordinal.Compare(left.Uuid, right.Uuid);
    }

    private static FieldInfo? FindField(Type type, string name, bool isStatic)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var field = current.GetField(name, flags);
            if (field is not null) return field;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

    private AutoSpellLevelSnapshot BlockSnapshot(string message, out string reason)
    {
        Block(message, out reason);
        return new AutoSpellLevelSnapshot(AutoSpellLevelCapability.Locked, null);
    }

    private bool HandleRegistryFailure(
        string label,
        TypedRegistryResolution resolution,
        out string reason)
    {
        var message = $"{label} resolution failed. {resolution.Format()}";
        return resolution.IsRetryable
            ? Retry(message, out reason)
            : Block(message, out reason);
    }

    private bool Block(string message, out string reason)
    {
        _blockedReason = message;
        reason = message;
        return false;
    }

    private static bool Retry(string message, out string reason)
    {
        reason = message;
        return false;
    }

    private static bool Reject(string message, out string reason)
    {
        reason = message;
        return false;
    }

    private static bool IsReflectionFailure(Exception ex) =>
        ex is TargetInvocationException or ArgumentException or InvalidOperationException or InvalidCastException or OverflowException;
}
