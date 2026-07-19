using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoBuyResourceDefinition
{
    public AutoBuyResourceDefinition(
        string resourceId,
        string resourceName,
        object nativeResource,
        BigAmount nominalCost)
    {
        ResourceId = resourceId;
        ResourceName = resourceName;
        NativeResource = nativeResource;
        NominalCost = nominalCost;
    }

    public string ResourceId { get; }

    public string ResourceName { get; }

    public object NativeResource { get; }

    public BigAmount NominalCost { get; set; }
}

internal readonly struct AutoBuyResourceSnapshot
{
    public AutoBuyResourceSnapshot(
        string resourceId,
        object nativeResource,
        BigAmount storedQuantity,
        BigAmount trueQuantity,
        BigAmount quality,
        BigAmount effectiveAttributeCost,
        BigAmount? capacity,
        bool isAvailable,
        bool isBandwidth,
        long epoch)
    {
        ResourceId = resourceId;
        NativeResource = nativeResource;
        StoredQuantity = storedQuantity;
        TrueQuantity = trueQuantity;
        Quality = quality;
        EffectiveAttributeCost = effectiveAttributeCost;
        Capacity = capacity;
        IsAvailable = isAvailable;
        IsBandwidth = isBandwidth;
        Epoch = epoch;
    }

    public string ResourceId { get; }

    public object NativeResource { get; }

    public BigAmount StoredQuantity { get; }

    public BigAmount TrueQuantity { get; }

    public BigAmount Quality { get; }

    public BigAmount EffectiveAttributeCost { get; }

    public BigAmount? Capacity { get; }

    public bool IsAvailable { get; }

    public bool IsBandwidth { get; }

    public long Epoch { get; }
}

internal interface IAutoBuyResourceSnapshotReader
{
    bool TryRead(AutoBuyResourceDefinition definition, long epoch, out AutoBuyResourceSnapshot snapshot);
}

internal sealed class AutoBuyResourceSnapshotCache
{
    private readonly Dictionary<string, Entry> _entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly IAutoBuyResourceSnapshotReader _reader;
    private readonly Action<string, AutoBuyResourceChange, BigAmount?, BigAmount?> _onChanged;
    private readonly List<string> _staleResourceIds = new List<string>();
    private long _epoch;

    public AutoBuyResourceSnapshotCache(
        IAutoBuyResourceSnapshotReader reader,
        Action<string, AutoBuyResourceChange, BigAmount?, BigAmount?> onChanged)
    {
        _reader = reader;
        _onChanged = onChanged;
    }

    public long Epoch => _epoch;

    public void BeginEvaluationEpoch(Func<string, bool> isTracked)
    {
        _epoch++;
        _staleResourceIds.Clear();
        foreach (var resourceId in _entries.Keys)
        {
            if (!isTracked(resourceId))
            {
                _staleResourceIds.Add(resourceId);
            }
        }

        for (var i = 0; i < _staleResourceIds.Count; i++)
        {
            _entries.Remove(_staleResourceIds[i]);
        }

        foreach (var entry in _entries.Values)
        {
            ReadEntry(entry);
        }
    }

    public void BeginLazyEpoch()
    {
        _epoch++;
    }

    public bool TryResolve(
        AutoBuyResourceDefinition definition,
        out AutoBuyResourceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(definition.ResourceId))
        {
            snapshot = default;
            return false;
        }

        if (!_entries.TryGetValue(definition.ResourceId, out var entry))
        {
            entry = new Entry(definition);
            _entries.Add(definition.ResourceId, entry);
        }
        else if (!ReferenceEquals(entry.Definition.NativeResource, definition.NativeResource))
        {
            entry.Definition = definition;
            entry.HasSnapshot = false;
            entry.ReadEpoch = 0;
            entry.Circuit = new AutomationCircuitBreaker();
            _onChanged(definition.ResourceId, AutoBuyResourceChange.Identity, null, null);
        }
        else
        {
            entry.Definition = definition;
        }

        if (entry.ReadEpoch != _epoch)
        {
            ReadEntry(entry);
        }

        snapshot = entry.Snapshot;
        return entry.HasSnapshot && snapshot.IsAvailable;
    }

    public void Clear()
    {
        _entries.Clear();
        _epoch++;
    }

    public bool NotifyAuthoritativeChange(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            !_entries.TryGetValue(resourceId, out var entry) ||
            !entry.Circuit.Wake(
                AutomationRetryTrigger.ResourceQuantity |
                AutomationRetryTrigger.ResourceRate,
                _epoch,
                lifecycleGeneration: 0))
        {
            return false;
        }

        entry.ReadEpoch = 0;
        return true;
    }

    private void ReadEntry(Entry entry)
    {
        entry.ReadEpoch = _epoch;
        if (entry.Circuit.IsOpen && !entry.Circuit.CanAttemptAt(_epoch))
        {
            return;
        }

        var hadSnapshot = entry.HasSnapshot;
        var previous = entry.Snapshot;
        if (!_reader.TryRead(entry.Definition, _epoch, out var current))
        {
            entry.HasSnapshot = false;
            entry.Snapshot = default;
            var nextRetryEpoch = entry.Circuit.RetryAfterTime(
                AutomationDecisionCode.NativeStateUnavailable,
                AutomationRetryTrigger.ResourceQuantity |
                AutomationRetryTrigger.ResourceRate |
                AutomationRetryTrigger.Registry |
                AutomationRetryTrigger.Lifecycle,
                _epoch,
                lifecycleGeneration: 0);
            _onChanged(
                entry.Definition.ResourceId,
                hadSnapshot ? AutoBuyResourceChange.Unknown | AutoBuyResourceChange.Identity : AutoBuyResourceChange.Unknown,
                hadSnapshot ? previous.TrueQuantity : null,
                null);
            if (entry.Circuit.Snapshot.ConsecutiveFailures == 1 || _epoch - entry.LastWarningEpoch >= 64)
            {
                entry.LastWarningEpoch = _epoch;
                Plugin.Log?.LogAutomataWarning(
                    $"Auto Buy quarantined resource snapshot {entry.Definition.ResourceId}; " +
                    $"retryEpoch={nextRetryEpoch}.");
            }
            return;
        }

        entry.HasSnapshot = true;
        entry.Snapshot = current;
        entry.Circuit.RecordSuccess();
        var change = hadSnapshot ? Compare(previous, current) : AutoBuyResourceChange.Identity;
        if (change != AutoBuyResourceChange.None)
        {
            _onChanged(
                entry.Definition.ResourceId,
                change,
                hadSnapshot ? previous.TrueQuantity : null,
                current.TrueQuantity);
        }
    }

    private static AutoBuyResourceChange Compare(
        AutoBuyResourceSnapshot previous,
        AutoBuyResourceSnapshot current)
    {
        var change = AutoBuyResourceChange.None;
        if (!ReferenceEquals(previous.NativeResource, current.NativeResource) ||
            !string.Equals(previous.ResourceId, current.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            change |= AutoBuyResourceChange.Identity;
        }

        if (previous.StoredQuantity.CompareTo(current.StoredQuantity) != 0 ||
            previous.TrueQuantity.CompareTo(current.TrueQuantity) != 0)
        {
            change |= AutoBuyResourceChange.Quantity;
        }

        if (previous.Quality.CompareTo(current.Quality) != 0)
        {
            change |= AutoBuyResourceChange.Quality;
        }

        if (previous.EffectiveAttributeCost.CompareTo(current.EffectiveAttributeCost) != 0)
        {
            change |= AutoBuyResourceChange.AttributeCost;
        }

        if (previous.IsAvailable != current.IsAvailable)
        {
            change |= AutoBuyResourceChange.Availability;
        }

        if (NullableCompare(previous.Capacity, current.Capacity) != 0)
        {
            change |= AutoBuyResourceChange.Capacity;
        }

        return change;
    }

    private static int NullableCompare(BigAmount? left, BigAmount? right)
    {
        if (!left.HasValue)
        {
            return right.HasValue ? -1 : 0;
        }

        return right.HasValue ? left.Value.CompareTo(right.Value) : 1;
    }

    private sealed class Entry
    {
        public Entry(AutoBuyResourceDefinition definition)
        {
            Definition = definition;
        }

        public AutoBuyResourceDefinition Definition { get; set; }

        public AutoBuyResourceSnapshot Snapshot { get; set; }

        public bool HasSnapshot { get; set; }

        public long ReadEpoch { get; set; }

        public AutomationCircuitBreaker Circuit { get; set; } = new AutomationCircuitBreaker();

        public long LastWarningEpoch { get; set; } = long.MinValue;
    }
}

internal sealed class ReflectionAutoBuyResourceSnapshotReader : IAutoBuyResourceSnapshotReader
{
    private readonly Dictionary<Type, Accessors?> _accessors = new Dictionary<Type, Accessors?>();

    public bool TryRead(
        AutoBuyResourceDefinition definition,
        long epoch,
        out AutoBuyResourceSnapshot snapshot)
    {
        snapshot = default;
        var resource = definition.NativeResource;
        if (resource is null || resource is UnityEngine.Object unityObject && unityObject == null)
        {
            return false;
        }

        var resourceType = resource.GetType();
        if (!_accessors.TryGetValue(resourceType, out var accessors))
        {
            accessors = Accessors.TryCreate(resourceType);
            _accessors.Add(resourceType, accessors);
        }

        if (accessors is null ||
            !string.Equals(ReflectionUtil.ReadStableId(resource), definition.ResourceId, StringComparison.OrdinalIgnoreCase) ||
            !TryInvokeAmount(accessors.GetQuantity, resource, out var storedQuantity) ||
            !TryInvokeAmount(accessors.GetTrueQuantity, resource, out var trueQuantity) ||
            !TryInvokeAmount(accessors.GetAttributeCostMod, resource, out var attributeCost) ||
            !TryInvokeBool(accessors.IsAvailable, resource, out var isAvailable) ||
            !TryInvokeBool(accessors.IsBandwidthResource, resource, out var isBandwidth) ||
            !TryInvokeAmount(accessors.ModifierGetValue, accessors.QualityField.GetValue(resource), out var quality))
        {
            return false;
        }

        var capacityRecord = accessors.MaxQuantityField.GetValue(resource);
        if (capacityRecord is null ||
            !TryInvokeOneAmount(
                accessors.GetTrueAmount,
                accessors.ModifierGetValue,
                resource,
                capacityRecord,
                out var trueCapacity))
        {
            return false;
        }

        snapshot = new AutoBuyResourceSnapshot(
            definition.ResourceId,
            resource,
            storedQuantity,
            trueQuantity,
            quality,
            attributeCost,
            trueCapacity,
            isAvailable,
            isBandwidth,
            epoch);
        return true;
    }

    private static bool TryInvokeAmount(MethodInfo? method, object? instance, out BigAmount value)
    {
        value = default;
        try
        {
            return method is not null && instance is not null &&
                   BigAmount.TryRead(method.Invoke(instance, Array.Empty<object>()), out value);
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryInvokeBool(MethodInfo? method, object instance, out bool value)
    {
        value = false;
        try
        {
            if (method?.Invoke(instance, Array.Empty<object>()) is bool result)
            {
                value = result;
                return true;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
        }

        return false;
    }

    private static bool TryInvokeOneAmount(
        MethodInfo? method,
        MethodInfo? getValue,
        object instance,
        object capacityRecord,
        out BigAmount value)
    {
        value = default;
        if (method is null)
        {
            return false;
        }

        // ValueModifierRecord.GetValue() was already validated above. Invoke it
        // again only for capacity conversion; GetTrueQuantity remains one read.
        try
        {
            var nativeCapacity = getValue?.Invoke(capacityRecord, Array.Empty<object>());
            return nativeCapacity is not null &&
                   BigAmount.TryRead(method.Invoke(instance, new[] { nativeCapacity }), out value);
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    private sealed class Accessors
    {
        private Accessors(
            MethodInfo getQuantity,
            MethodInfo getTrueQuantity,
            MethodInfo getAttributeCostMod,
            MethodInfo isAvailable,
            MethodInfo isBandwidthResource,
            MethodInfo getTrueAmount,
            MethodInfo modifierGetValue,
            FieldInfo qualityField,
            FieldInfo maxQuantityField)
        {
            GetQuantity = getQuantity;
            GetTrueQuantity = getTrueQuantity;
            GetAttributeCostMod = getAttributeCostMod;
            IsAvailable = isAvailable;
            IsBandwidthResource = isBandwidthResource;
            GetTrueAmount = getTrueAmount;
            ModifierGetValue = modifierGetValue;
            QualityField = qualityField;
            MaxQuantityField = maxQuantityField;
        }

        public MethodInfo GetQuantity { get; }

        public MethodInfo GetTrueQuantity { get; }

        public MethodInfo GetAttributeCostMod { get; }

        public MethodInfo IsAvailable { get; }

        public MethodInfo IsBandwidthResource { get; }

        public MethodInfo GetTrueAmount { get; }

        public MethodInfo ModifierGetValue { get; }

        public FieldInfo QualityField { get; }

        public FieldInfo MaxQuantityField { get; }

        public static Accessors? TryCreate(Type type)
        {
            if (!HasResourceBaseType(type))
            {
                return null;
            }

            var getQuantity = FindNoArg(type, "GetQuantity");
            var getTrueQuantity = FindNoArg(type, "GetTrueQuantity");
            var getAttributeCostMod = FindNoArg(type, "GetAttributeCostMod");
            var isAvailable = type.GetMethod("IsAvailable", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            var isBandwidthResource = type.GetMethod(
                "IsBandwidthResource",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            var quality = type.GetField("quality", ReflectionUtil.InstanceFlags);
            var maxQuantity = type.GetField("maxQuantity", ReflectionUtil.InstanceFlags);
            MethodInfo? getTrueAmount = null;
            foreach (var method in type.GetMethods(ReflectionUtil.InstanceFlags))
            {
                if (string.Equals(method.Name, "GetTrueAmount", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 1)
                {
                    getTrueAmount = method;
                    break;
                }
            }
            var modifierGetValue = quality?.FieldType.GetMethod(
                "GetValue",
                ReflectionUtil.InstanceFlags,
                null,
                Type.EmptyTypes,
                null);
            if (getQuantity is null || getTrueQuantity is null || getAttributeCostMod is null ||
                isAvailable?.ReturnType != typeof(bool) ||
                isBandwidthResource?.ReturnType != typeof(bool) ||
                quality is null || maxQuantity is null ||
                getTrueAmount is null || getTrueAmount.GetParameters().Length != 1 ||
                modifierGetValue is null)
            {
                return null;
            }

            return new Accessors(
                getQuantity,
                getTrueQuantity,
                getAttributeCostMod,
                isAvailable,
                isBandwidthResource,
                getTrueAmount,
                modifierGetValue,
                quality,
                maxQuantity);
        }

        private static MethodInfo? FindNoArg(Type type, string name)
        {
            return type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
        }

        private static bool HasResourceBaseType(Type type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (string.Equals(current.Name, "ResourceSO", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
