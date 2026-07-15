using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OrbAutomata;

internal static class ReflectionCostReader
{
    public static IReadOnlyList<ResourceAdmissionCost> Read(object? container)
    {
        if (container is null)
        {
            return Array.Empty<ResourceAdmissionCost>();
        }

        var costs = new List<ResourceAdmissionCost>();
        foreach (var item in FlattenCostObjects(container, 0).Take(128))
        {
            if (!TryReadResourceAndAmount(item, out var resource, out var amount) ||
                !TryReadQuantity(resource, out var quantity))
            {
                continue;
            }

            costs.Add(new ResourceAdmissionCost(
                ReflectionUtil.ReadStableId(resource) ?? ObjectName(resource),
                ReflectionUtil.ReadDisplayName(resource) ?? ObjectName(resource),
                amount,
                quantity,
                TryReadCapacity(resource, out var capacity) ? capacity : null));
        }

        return costs
            .GroupBy(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new ResourceAdmissionCost(
                    first.ResourceId,
                    first.ResourceName,
                    group.Select(cost => cost.Cost).Aggregate(default(BigAmount), (sum, item) => sum.Add(item)),
                    first.CurrentQuantity,
                    first.Capacity);
            })
            .ToArray();
    }

    private static IEnumerable<object> FlattenCostObjects(object value, int depth)
    {
        if (depth > 3 || value is string)
        {
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                foreach (var nested in FlattenCostObjects(item, depth + 1))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        yield return value;
        foreach (var memberValue in ReflectionUtil.ReadLikelyCollectionMembers(value))
        {
            foreach (var nested in FlattenCostObjects(memberValue, depth + 1))
            {
                yield return nested;
            }
        }
    }

    private static bool TryReadResourceAndAmount(object item, out object resource, out BigAmount amount)
    {
        resource = null!;
        amount = default;

        foreach (var member in ReflectionUtil.ReadAllMembers(item))
        {
            if (member.Value is null)
            {
                continue;
            }

            if (IsResourceLike(member.Value) && resource is null)
            {
                resource = member.Value;
                continue;
            }

            if (IsAmountName(member.Name) && BigAmount.TryRead(member.Value, out var candidateAmount))
            {
                amount = candidateAmount;
            }
        }

        if (amount.IsZero)
        {
            var nativeValue = ReflectionUtil.InvokeNoArgs(item, "GetValue");
            BigAmount.TryRead(nativeValue, out amount);
        }

        return resource is not null && !amount.IsZero;
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
