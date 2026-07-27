using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using OrbModding.Common;

namespace OrbAutomata;

internal static class ReflectionCostReader
{
    public static IReadOnlyList<ResourceAdmissionCost> Read(object? container)
    {
        return TryRead(container, out var costs, out _) ? costs : Array.Empty<ResourceAdmissionCost>();
    }

    public static bool TryRead(
        object? container,
        out IReadOnlyList<ResourceAdmissionCost> costs,
        out string reason)
    {
        if (container is null)
        {
            costs = Array.Empty<ResourceAdmissionCost>();
            reason = "native cost container is unavailable";
            return false;
        }

        var combined = new List<(object Resource, ResourceAdmissionCost Cost)>();
        var entryCount = 0;
        if (!TryTraverseCostContainer(container, 0, combined, ref entryCount, out reason))
        {
            costs = Array.Empty<ResourceAdmissionCost>();
            return false;
        }

        var result = new ResourceAdmissionCost[combined.Count];
        for (var index = 0; index < combined.Count; index++) result[index] = combined[index].Cost;
        costs = result;
        reason = string.Empty;
        return true;
    }

    private static bool TryTraverseCostContainer(
        object value,
        int depth,
        List<(object Resource, ResourceAdmissionCost Cost)> combined,
        ref int entryCount,
        out string reason)
    {
        if (depth > 3)
        {
            reason = "native cost container exceeded the audited nesting depth";
            return false;
        }

        if (value is string)
        {
            reason = "native cost container contained an unexpected string leaf";
            return false;
        }

        if (value is IEnumerable enumerable)
        {
            try
            {
                foreach (var item in enumerable)
                {
                    if (item is null)
                    {
                        reason = "native cost container contained a null entry";
                        return false;
                    }

                    if (!TryTraverseCostContainer(item, depth + 1, combined, ref entryCount, out reason))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                reason = $"native cost enumeration failed: {ex.GetBaseException().Message}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        if (!TryTraverseNamedCollections(value, depth, combined, ref entryCount, out var foundCollection, out reason))
        {
            return false;
        }
        if (foundCollection)
        {
            return true;
        }

        var costShape = TryReadResourceAndAmount(
            value,
            out var resource,
            out var amount,
            out var sawResource,
            out var sawAmount);
        if (costShape)
        {
            entryCount++;
            if (entryCount > 128)
            {
                reason = "native cost container exceeded the audited 128-entry bound";
                return false;
            }

            return TryAddDecodedCost(resource, amount, combined, out reason);
        }

        if (sawResource || sawAmount)
        {
            reason = $"native cost entry {value.GetType().FullName ?? value.GetType().Name} could not be decoded completely";
            return false;
        }

        var type = value.GetType();
        reason = $"native cost leaf {type.FullName ?? type.Name} has no audited cost shape";
        return false;
    }

    private static bool TryTraverseNamedCollections(
        object value,
        int depth,
        List<(object Resource, ResourceAdmissionCost Cost)> combined,
        ref int entryCount,
        out bool foundCollection,
        out string reason)
    {
        foundCollection = false;
        var type = value.GetType();
        foreach (var field in type.GetFields(ReflectionUtil.InstanceFlags))
        {
            if (!IsCollectionMember(field.Name, field.FieldType)) continue;
            foundCollection = true;
            object? memberValue;
            try
            {
                memberValue = field.GetValue(value);
            }
            catch (Exception ex)
            {
                reason = $"native cost collection member {field.Name} could not be read: {ex.GetBaseException().Message}";
                return false;
            }
            if (memberValue is null)
            {
                reason = $"native cost collection member {field.Name} is null";
                return false;
            }
            if (!TryTraverseCostContainer(memberValue, depth + 1, combined, ref entryCount, out reason))
            {
                return false;
            }
        }

        foreach (var property in type.GetProperties(ReflectionUtil.InstanceFlags))
        {
            if (property.GetIndexParameters().Length > 0 || !IsCollectionMember(property.Name, property.PropertyType)) continue;
            foundCollection = true;
            object? memberValue;
            try
            {
                memberValue = property.GetValue(value);
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
            {
                reason = $"native cost collection member {property.Name} could not be read: {ex.GetBaseException().Message}";
                return false;
            }
            if (memberValue is null)
            {
                reason = $"native cost collection member {property.Name} is null";
                return false;
            }
            if (!TryTraverseCostContainer(memberValue, depth + 1, combined, ref entryCount, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryAddDecodedCost(
        object resource,
        BigAmount amount,
        List<(object Resource, ResourceAdmissionCost Cost)> combined,
        out string reason)
    {
        if (!TryReadQuantity(resource, out var quantity))
        {
            reason = "native cost resource quantity is unavailable";
            return false;
        }

        var resourceId = ReflectionUtil.ReadStableId(resource);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            reason = "native cost resource has no stable UUID";
            return false;
        }

        var decoded = new ResourceAdmissionCost(
            resourceId,
            ReflectionUtil.ReadDisplayName(resource) ?? ObjectName(resource),
            amount,
            quantity,
            TryReadCapacity(resource, out var capacity) ? capacity : null);
        for (var index = 0; index < combined.Count; index++)
        {
            var existing = combined[index];
            if (!string.Equals(existing.Cost.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ReferenceEquals(existing.Resource, resource))
            {
                reason = $"native cost vector maps UUID {resourceId} to multiple resource objects";
                return false;
            }

            combined[index] = (resource, new ResourceAdmissionCost(
                existing.Cost.ResourceId,
                existing.Cost.ResourceName,
                existing.Cost.Cost.Add(amount),
                existing.Cost.CurrentQuantity,
                existing.Cost.Capacity));
            reason = string.Empty;
            return true;
        }

        combined.Add((resource, decoded));
        reason = string.Empty;
        return true;
    }

    private static bool TryReadResourceAndAmount(
        object item,
        out object resource,
        out BigAmount amount,
        out bool sawResource,
        out bool sawAmount)
    {
        resource = null!;
        amount = default;
        var amountRead = false;
        sawResource = false;
        sawAmount = false;

        foreach (var member in ReflectionUtil.ReadAllMembers(item))
        {
            if (member.Value is null)
            {
                continue;
            }

            if (IsResourceLike(member.Value) && resource is null)
            {
                resource = member.Value;
                sawResource = true;
                continue;
            }

            if (IsAmountName(member.Name) &&
                !string.Equals(member.Name, "costs", StringComparison.OrdinalIgnoreCase) &&
                member.Value is not IEnumerable)
            {
                sawAmount = true;
                if (BigAmount.TryRead(member.Value, out var candidateAmount))
                {
                    amount = candidateAmount;
                    amountRead = true;
                }
            }
        }

        if (!amountRead)
        {
            var nativeValue = ReflectionUtil.InvokeNoArgs(item, "GetValue");
            amountRead = BigAmount.TryRead(nativeValue, out amount);
        }

        return resource is not null && amountRead;
    }

    private static bool IsCollectionMember(string name, Type memberType)
    {
        if (memberType == typeof(string)) return false;
        if (string.Equals(name, "costs", StringComparison.OrdinalIgnoreCase)) return true;
        return typeof(IEnumerable).IsAssignableFrom(memberType) &&
               (name.Contains("cost", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("resource", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("list", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadQuantity(object resource, out BigAmount quantity)
    {
        foreach (var method in new[] { "GetTrueQuantity", "GetQuantity" })
        {
            if (BigAmount.TryRead(ReflectionUtil.InvokeNoArgs(resource, method), out quantity))
            {
                return true;
            }
        }

        foreach (var field in new[] { "quantity", "Quantity" })
        {
            if (BigAmount.TryRead(ReflectionUtil.ReadMember(resource, field), out quantity))
            {
                return true;
            }
        }

        quantity = default;
        return false;
    }

    private static bool TryReadCapacity(object resource, out BigAmount capacity)
    {
        var maxQuantityRecord = ReflectionUtil.ReadMember(resource, "maxQuantity") ??
                                ReflectionUtil.ReadMember(resource, "MaxQuantity");
        if (maxQuantityRecord is not null)
        {
            var nativeCapacity = ReflectionUtil.InvokeNoArgs(maxQuantityRecord, "GetValue") ??
                                 ReflectionUtil.InvokeNoArgs(maxQuantityRecord, "AsBigDouble") ??
                                 maxQuantityRecord;
            var trueCapacity = InvokeOneArgument(resource, "GetTrueAmount", nativeCapacity);
            if (BigAmount.TryRead(trueCapacity ?? nativeCapacity, out capacity))
            {
                return true;
            }
        }

        capacity = default;
        return false;
    }

    private static object? InvokeOneArgument(object instance, string name, object argument)
    {
        var method = instance.GetType()
            .GetMethods(ReflectionUtil.InstanceFlags)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument);
            });
        if (method is null)
        {
            return null;
        }

        try
        {
            return method.Invoke(instance, new[] { argument });
        }
        catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsResourceLike(object value)
    {
        var typeName = value.GetType().Name;
        return typeName.Contains("ResourceSO", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("Resource", StringComparison.OrdinalIgnoreCase) && value is ScriptableObject;
    }

    private static bool IsAmountName(string name)
    {
        return name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("quantity", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("cost", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("value", StringComparison.OrdinalIgnoreCase);
    }

    private static string ObjectName(object value)
    {
        return value is UnityEngine.Object unityObject ? unityObject.name : value.GetType().Name;
    }
}
