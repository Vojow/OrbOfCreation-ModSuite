using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OrbAutomata;

internal sealed class ReflectionAutoBuyCatalog : IAutoBuyCatalog
{
    public IEnumerable<IAutoBuyCandidate> Discover()
    {
        return EnumerateStaticList("StructureSO", "All", AutoBuyCandidateKind.Structure)
            .Concat(EnumerateStaticList("UpgradeSO", "All", AutoBuyCandidateKind.Upgrade))
            .OrderBy(candidate => candidate.Snapshot().Uuid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

internal sealed class ReflectionAutoBuyCandidate : IAutoBuyCandidate
{
    private readonly object _source;
    private readonly AutoBuyCandidateKind _kind;
    private AutoBuyCandidateSnapshot? _snapshot;

    public ReflectionAutoBuyCandidate(object source, AutoBuyCandidateKind kind)
    {
        _source = source;
        _kind = kind;
    }

    public AutoBuyCandidateSnapshot Snapshot()
    {
        return _snapshot ??= new AutoBuyCandidateSnapshot(
            this,
            ReflectionUtil.ReadStableId(_source) ?? $"{_kind}:{_source.GetType().Name}:{_source.GetHashCode()}",
            ReflectionUtil.ReadDisplayName(_source) ?? _source.GetType().Name,
            _kind,
            _source.GetType().FullName ?? _source.GetType().Name);
    }

    public bool IsAvailable()
    {
        return ReflectionUtil.TryInvokeBool(_source, out var available, "IsAvailable") && available;
    }

    public bool CanPurchase(out string reason)
    {
        if (!ReflectionUtil.TryInvokeBool(_source, out var canPurchase, "CanPurchase"))
        {
            reason = "CanPurchase unavailable";
            return false;
        }

        reason = canPurchase ? string.Empty : "native CanPurchase returned false";
        return canPurchase;
    }

    public IReadOnlyList<ResourceAdmissionCost> GetCosts()
    {
        var container = ReflectionUtil.InvokeNoArgs(_source, "GetPurchaseCost") ??
                        ReflectionUtil.InvokeNoArgs(_source, "GetResourceCost") ??
                        ReflectionUtil.InvokeNoArgs(_source, "GetNextCost");
        return ReflectionCostReader.Read(container);
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
        var method = _source.GetType().GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, new[] { typeof(bool) }, null);
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
        var method = _source.GetType().GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
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
