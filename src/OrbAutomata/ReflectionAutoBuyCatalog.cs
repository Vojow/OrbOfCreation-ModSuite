using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace OrbAutomata;

internal sealed class ReflectionAutoBuyCatalog : IAutoBuyCatalog
{
    private static readonly TimeSpan RegistryReconciliationInterval = TimeSpan.FromSeconds(10);
    private readonly AutoBuyCandidateIndex _index = new AutoBuyCandidateIndex();
    private readonly Stopwatch _lifetime = Stopwatch.StartNew();
    private IReadOnlyList<IAutoBuyCandidate>? _registeredCandidates;
    private TimeSpan _nextRegistryReconciliation;

    public IEnumerable<IAutoBuyCandidate> Discover()
    {
        if (_registeredCandidates is null || _lifetime.Elapsed >= _nextRegistryReconciliation)
        {
            _registeredCandidates = EnumerateStaticList("StructureSO", "All", AutoBuyCandidateKind.Structure)
                .Concat(EnumerateStaticList("UpgradeSO", "All", AutoBuyCandidateKind.Upgrade))
                .OrderBy(candidate => candidate.Snapshot().Uuid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _nextRegistryReconciliation = _lifetime.Elapsed + RegistryReconciliationInterval;
        }

        return _index.Reconcile(_registeredCandidates);
    }

    public bool TryGetRemainingQueueRoom(out int remainingRoom)
    {
        remainingRoom = 0;
        var type = ReflectionUtil.FindLoadedType("ActionManager");
        var method = type?.GetMethod("GetRemainingRoom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var value = method?.Invoke(null, Array.Empty<object>());
            if (value is int room)
            {
                remainingRoom = room;
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public bool TryGetBulkDevelopment(out int levels)
    {
        levels = 1;
        var player = ReflectionUtil.FindLoadedType("Player");
        var method = player?.GetMethod("GetBulkDevelopment", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var variable = method?.Invoke(null, Array.Empty<object>());
            if (variable is not null && ReflectionUtil.TryReadNumeric(variable, out var value, "AsInt"))
            {
                levels = Math.Max(1, (int)value);
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public bool TryGetActionMultiplier(out int multiplier)
    {
        multiplier = 1;
        var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
        var method = globals?.GetMethod("GetMultiBuy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        try
        {
            var variable = method?.Invoke(null, Array.Empty<object>());
            if (variable is not null && ReflectionUtil.TryReadNumeric(variable, out var value, "AsInt"))
            {
                multiplier = Math.Max(1, (int)value);
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    public void Dispose()
    {
        _registeredCandidates = null;
        _index.Clear();
    }

    private static IEnumerable<IAutoBuyCandidate> EnumerateStaticList(
        string typeName,
        string memberName,
        AutoBuyCandidateKind kind)
    {
        var type = ReflectionUtil.FindLoadedType(typeName);
        if (type is null)
        {
            return Array.Empty<IAutoBuyCandidate>();
        }

        object? value = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ??
                        type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
        if (value is not IEnumerable items)
        {
            return Array.Empty<IAutoBuyCandidate>();
        }

        return items.Cast<object?>()
            .Where(item => item is not null)
            .Select(item => (IAutoBuyCandidate)new ReflectionAutoBuyCandidate(item!, kind))
            .ToArray();
    }
}

internal sealed class ReflectionAutoBuyCandidate : IAutoBuyCandidate, IAutoBuyLifecycleCandidate, IAutoBuyNativeIdentity
{
    private readonly object _source;
    private readonly AutoBuyCandidateKind _kind;
    private readonly Type _sourceType;
    private readonly MethodInfo? _isAvailable;
    private readonly MethodInfo? _canPurchase;
    private readonly MethodInfo? _getPurchaseCost;
    private readonly MethodInfo? _purchase;
    private readonly MethodInfo? _getPurchaseLevel;
    private readonly MethodInfo? _getQueuedState;
    private readonly MethodInfo? _hasFiniteLevels;
    private readonly MethodInfo? _isMaxLevel;
    private readonly MethodInfo? _isMaxQueuedLevel;
    private readonly bool _expectedNativeType;
    private AutoBuyCandidateSnapshot? _snapshot;

    public ReflectionAutoBuyCandidate(object source, AutoBuyCandidateKind kind)
    {
        _source = source;
        _kind = kind;
        _sourceType = source.GetType();
        _expectedNativeType = HasExpectedNativeType(_sourceType, kind);
        _isAvailable = FindNoArgMethod("IsAvailable", typeof(bool));
        _canPurchase = FindNoArgMethod("CanPurchase", typeof(bool));
        _getPurchaseCost = FindNoArgMethod("GetPurchaseCost", null);
        _purchase = kind == AutoBuyCandidateKind.Structure
            ? _sourceType.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, new[] { typeof(bool) }, null)
            : _sourceType.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
        _getPurchaseLevel = FindNoArgMethod("GetPurchaseLevel", typeof(int));
        _getQueuedState = FindNoArgMethod(
            kind == AutoBuyCandidateKind.Structure ? "GetQueuedQuantity" : "GetQueuedPurchaseLevel",
            typeof(int));
        if (kind == AutoBuyCandidateKind.Upgrade)
        {
            _hasFiniteLevels = FindNoArgMethod("HasFiniteLevels", typeof(bool));
            _isMaxLevel = FindNoArgMethod("IsMaxLevel", typeof(bool));
            _isMaxQueuedLevel = FindNoArgMethod("IsMaxQueuedLevel", typeof(bool));
        }
    }

    public object NativeIdentity => _source;

    public AutoBuyCandidateSnapshot Snapshot()
    {
        return _snapshot ??= new AutoBuyCandidateSnapshot(
            this,
            ReflectionUtil.ReadStableId(_source) ?? string.Empty,
            ReflectionUtil.ReadDisplayName(_source) ?? _sourceType.Name,
            _kind,
            _sourceType.FullName ?? _sourceType.Name);
    }

    public bool IsAvailable()
    {
        return TryInvoke(_isAvailable, out bool available) && available;
    }

    public bool CanPurchase(out string reason)
    {
        if (!TryInvoke(_canPurchase, out bool canPurchase))
        {
            reason = "CanPurchase unavailable";
            return false;
        }

        reason = canPurchase ? string.Empty : "native CanPurchase returned false";
        return canPurchase;
    }

    public IReadOnlyList<ResourceAdmissionCost> GetCosts()
    {
        var container = Invoke(_getPurchaseCost);
        return ReflectionCostReader.Read(container);
    }

    public bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason)
    {
        evidence = default;
        if (!_expectedNativeType)
        {
            reason = $"native object is not an audited {_kind} type";
            return false;
        }

        if (_source is UnityEngine.Object unityObject && unityObject == null)
        {
            reason = "native Unity object was destroyed";
            return false;
        }

        if (!TryInvoke(_isAvailable, out bool available) ||
            !TryInvoke(_getPurchaseLevel, out int currentLevel) ||
            !TryInvoke(_getQueuedState, out int queuedValue))
        {
            reason = "required native lifecycle method was unavailable";
            return false;
        }

        if (_kind == AutoBuyCandidateKind.Structure)
        {
            evidence = new AutoBuyLifecycleEvidence(
                available,
                currentLevel,
                queuedValue,
                hasFiniteLevels: false,
                isMaxLevel: false,
                isMaxQueuedLevel: false);
            reason = string.Empty;
            return true;
        }

        if (!TryInvoke(_hasFiniteLevels, out bool finite) ||
            !TryInvoke(_isMaxLevel, out bool maxLevel) ||
            !TryInvoke(_isMaxQueuedLevel, out bool maxQueued))
        {
            reason = "required finite Upgrade lifecycle method was unavailable";
            return false;
        }

        var queuedLevels = queuedValue - currentLevel;
        evidence = new AutoBuyLifecycleEvidence(
            available,
            currentLevel,
            queuedLevels,
            finite,
            maxLevel,
            maxQueued);
        reason = string.Empty;
        return true;
    }

    public bool TryPurchaseOne(out string reason)
    {
        reason = string.Empty;
        if (!CanPurchase(out reason))
        {
            return false;
        }

        return _kind == AutoBuyCandidateKind.Structure
            ? TryPurchaseStructure(out reason)
            : TryPurchaseUpgrade(out reason);
    }

    private bool TryPurchaseStructure(out string reason)
    {
        reason = string.Empty;
        var method = _purchase;
        if (method is null)
        {
            reason = "Purchase(bool forceOne) unavailable";
            return false;
        }

        return InvokeAndVerify(method, new object[] { true }, "GetQueuedQuantity", out reason);
    }

    private bool TryPurchaseUpgrade(out string reason)
    {
        reason = string.Empty;
        var method = _purchase;
        if (method is null)
        {
            reason = "Purchase() unavailable";
            return false;
        }

        if (!NativeMultiBuyScope.TryEnterOne(out var scope, out reason))
        {
            return false;
        }

        using (scope)
        {
            return InvokeAndVerify(method, Array.Empty<object>(), "GetQueuedPurchaseLevel", out reason);
        }
    }

    private bool InvokeAndVerify(MethodInfo method, object[] arguments, string levelMethod, out string reason)
    {
        reason = string.Empty;
        ReflectionUtil.TryReadNumeric(_source, out var before, levelMethod);
        try
        {
            method.Invoke(_source, arguments);
        }
        catch (TargetInvocationException ex)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }

        if (!ReflectionUtil.TryReadNumeric(_source, out var after, levelMethod) || after <= before)
        {
            reason = $"native purchase did not increase {levelMethod}";
            return false;
        }

        return true;
    }

    private MethodInfo? FindNoArgMethod(string name, Type? returnType)
    {
        var method = _sourceType.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
        return method is not null && (returnType is null || method.ReturnType == returnType) ? method : null;
    }

    private object? Invoke(MethodInfo? method)
    {
        try
        {
            return method?.Invoke(_source, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private bool TryInvoke<T>(MethodInfo? method, out T value)
    {
        var result = Invoke(method);
        if (result is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private static bool HasExpectedNativeType(Type type, AutoBuyCandidateKind kind)
    {
        var expected = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.Name, expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class NativeMultiBuyScope : IDisposable
{
    private readonly object _variable;
    private readonly MethodInfo _setValue;
    private readonly int _originalValue;
    private bool _disposed;

    private NativeMultiBuyScope(object variable, MethodInfo setValue, int originalValue)
    {
        _variable = variable;
        _setValue = setValue;
        _originalValue = originalValue;
    }

    public static bool TryEnterOne(out NativeMultiBuyScope scope, out string reason)
    {
        scope = null!;
        reason = string.Empty;
        var globals = ReflectionUtil.FindLoadedType("GlobalVariables");
        var getMultiBuy = globals?.GetMethod("GetMultiBuy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        object? variable;
        try
        {
            variable = getMultiBuy?.Invoke(null, Array.Empty<object>());
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }

        if (variable is null || !ReflectionUtil.TryReadNumeric(variable, out var original, "AsInt"))
        {
            reason = "global multi-buy value unavailable";
            return false;
        }

        var setValue = variable.GetType().GetMethod("SetValue", ReflectionUtil.InstanceFlags, null, new[] { typeof(int) }, null);
        if (setValue is null)
        {
            reason = "global multi-buy SetValue(int) unavailable";
            return false;
        }

        try
        {
            setValue.Invoke(variable, new object[] { 1 });
            scope = new NativeMultiBuyScope(variable, setValue, (int)original);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _setValue.Invoke(_variable, new object[] { _originalValue });
        }
        catch
        {
            Plugin.Log?.LogError("Automata could not restore the global multi-buy value after an UpgradeSO purchase.");
        }
    }
}
