using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OrbAutomata;

internal readonly struct DecodedResourceCost
{
    public DecodedResourceCost(string resourceId, object nativeResource, BigAmount amount)
    {
        ResourceId = resourceId;
        NativeResource = nativeResource;
        Amount = amount;
    }

    public string ResourceId { get; }

    public object NativeResource { get; }

    public BigAmount Amount { get; }
}

internal enum NativeResourceCostReadFailureKind
{
    None,
    TransientState,
    Contract,
}

internal static class NativeResourceCostAdapter
{
    private static readonly Dictionary<Type, Accessors?> AccessorsByContainerType =
        new Dictionary<Type, Accessors?>();
    private static readonly Dictionary<Type, ResourceAccessors?> ResourceAccessorsByType =
        new Dictionary<Type, ResourceAccessors?>();

    internal static int CachedSchemaCount => AccessorsByContainerType.Count;

    public static bool TryRead(
        object container,
        List<DecodedResourceCost> destination,
        out int tupleCount,
        out string reason)
    {
        return TryRead(container, destination, out tupleCount, out reason, out _);
    }

    public static bool TryRead(
        object container,
        List<DecodedResourceCost> destination,
        out int tupleCount,
        out string reason,
        out NativeResourceCostReadFailureKind failureKind)
    {
        destination.Clear();
        tupleCount = 0;
        reason = string.Empty;
        failureKind = NativeResourceCostReadFailureKind.None;
        var type = container.GetType();
        if (!AccessorsByContainerType.TryGetValue(type, out var accessors))
        {
            accessors = Accessors.TryCreate(type);
            AccessorsByContainerType.Add(type, accessors);
        }

        if (accessors is null)
        {
            reason = "native ResourceCostList schema is not audited";
            failureKind = NativeResourceCostReadFailureKind.Contract;
            return false;
        }

        IList? tuples;
        try
        {
            tuples = accessors.CostsField.GetValue(container) as IList;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is TargetException)
        {
            reason = "native ResourceCostList entries are unreadable";
            failureKind = NativeResourceCostReadFailureKind.TransientState;
            return false;
        }

        if (tuples is null)
        {
            reason = "native ResourceCostList entries are unavailable";
            failureKind = NativeResourceCostReadFailureKind.TransientState;
            return false;
        }

        tupleCount = tuples.Count;
        for (var i = 0; i < tuples.Count; i++)
        {
            var tuple = tuples[i];
            if (tuple is null || tuple.GetType() != accessors.TupleType)
            {
                destination.Clear();
                reason = "native ResourceTuple type is contradictory";
                failureKind = NativeResourceCostReadFailureKind.Contract;
                return false;
            }

            object? resource;
            object? nativeAmount;
            try
            {
                resource = accessors.ResourceField.GetValue(tuple);
                nativeAmount = accessors.GetValue.Invoke(tuple, Array.Empty<object>());
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is TargetException || ex is TargetInvocationException)
            {
                destination.Clear();
                reason = "native ResourceTuple could not be decoded";
                failureKind = NativeResourceCostReadFailureKind.TransientState;
                return false;
            }

            if (resource is null || resource is UnityEngine.Object unityResource && unityResource == null)
            {
                destination.Clear();
                reason = "native ResourceTuple resource is unavailable";
                failureKind = NativeResourceCostReadFailureKind.TransientState;
                return false;
            }

            if (!HasResourceBaseType(resource.GetType()) || !BigAmount.TryRead(nativeAmount, out var amount))
            {
                destination.Clear();
                reason = "native ResourceTuple resource type or amount contract is invalid";
                failureKind = NativeResourceCostReadFailureKind.Contract;
                return false;
            }

            if (!TryReadResourceId(resource, out var resourceId, out var idContractFailure))
            {
                destination.Clear();
                reason = "native ResourceTuple resource UUID is unavailable";
                failureKind = idContractFailure
                    ? NativeResourceCostReadFailureKind.Contract
                    : NativeResourceCostReadFailureKind.TransientState;
                return false;
            }

            destination.Add(new DecodedResourceCost(resourceId, resource, amount));
        }

        return destination.Count == tupleCount;
    }

    public static string? ReadResourceName(object resource)
    {
        var accessors = GetResourceAccessors(resource.GetType());
        if (accessors is null)
        {
            return null;
        }

        try
        {
            return accessors.GetName.Invoke(resource, Array.Empty<object>()) as string;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is TargetException || ex is TargetInvocationException)
        {
            return null;
        }
    }

    private static bool TryReadResourceId(object resource, out string resourceId, out bool contractFailure)
    {
        resourceId = string.Empty;
        contractFailure = false;
        var accessors = GetResourceAccessors(resource.GetType());
        if (accessors is null)
        {
            contractFailure = true;
            return false;
        }

        try
        {
            var nativeId = accessors.GetGuid.Invoke(resource, Array.Empty<object>());
            resourceId = nativeId?.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(resourceId);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is TargetException || ex is TargetInvocationException)
        {
            return false;
        }
    }

    private static ResourceAccessors? GetResourceAccessors(Type type)
    {
        if (!ResourceAccessorsByType.TryGetValue(type, out var accessors))
        {
            accessors = ResourceAccessors.TryCreate(type);
            ResourceAccessorsByType.Add(type, accessors);
        }

        return accessors;
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

    private sealed class Accessors
    {
        private Accessors(Type tupleType, FieldInfo costsField, FieldInfo resourceField, MethodInfo getValue)
        {
            TupleType = tupleType;
            CostsField = costsField;
            ResourceField = resourceField;
            GetValue = getValue;
        }

        public Type TupleType { get; }

        public FieldInfo CostsField { get; }

        public FieldInfo ResourceField { get; }

        public MethodInfo GetValue { get; }

        public static Accessors? TryCreate(Type containerType)
        {
            if (!string.Equals(containerType.Name, "ResourceCostList", StringComparison.Ordinal))
            {
                return null;
            }

            var costsField = containerType.GetField("costs", ReflectionUtil.InstanceFlags);
            if (costsField is null || !typeof(IList).IsAssignableFrom(costsField.FieldType) ||
                !costsField.FieldType.IsGenericType)
            {
                return null;
            }

            var genericArguments = costsField.FieldType.GetGenericArguments();
            if (genericArguments.Length != 1 ||
                !string.Equals(genericArguments[0].Name, "ResourceTuple", StringComparison.Ordinal))
            {
                return null;
            }

            var tupleType = genericArguments[0];
            var resourceField = tupleType.GetField("resource", ReflectionUtil.InstanceFlags);
            var getValue = tupleType.GetMethod(
                "GetValue",
                ReflectionUtil.InstanceFlags,
                null,
                Type.EmptyTypes,
                null);
            if (resourceField is null ||
                !string.Equals(resourceField.FieldType.Name, "ResourceSO", StringComparison.Ordinal) ||
                getValue is null ||
                !getValue.ReturnType.Name.Contains("BigDouble", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new Accessors(tupleType, costsField, resourceField, getValue);
        }
    }

    private sealed class ResourceAccessors
    {
        private ResourceAccessors(MethodInfo getGuid, MethodInfo getName)
        {
            GetGuid = getGuid;
            GetName = getName;
        }

        public MethodInfo GetGuid { get; }

        public MethodInfo GetName { get; }

        public static ResourceAccessors? TryCreate(Type type)
        {
            if (!HasResourceBaseType(type))
            {
                return null;
            }

            var getGuid = type.GetMethod("GetGuid", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            var getName = type.GetMethod("GetName", ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return getGuid is null || getName?.ReturnType != typeof(string)
                ? null
                : new ResourceAccessors(getGuid, getName);
        }
    }
}
