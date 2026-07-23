using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class ServiceCycleTrustedWorkerStorageAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
internal sealed class ServiceCycleAuditedWorkerDependencyAttribute : Attribute
{
    internal ServiceCycleAuditedWorkerDependencyAttribute(bool required = true) => Required = required;
    internal bool Required { get; }
}

internal interface IServiceCycleAdditionalWorkerForbiddenTypeSource
{
    Type AdditionalWorkerForbiddenType { get; }
}

internal static class ServiceCycleWorkerDefinitionValidator
{
    internal static void EnsureSeparated<TFrame, TConfig, TState, TAction>(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> mainDefinition,
        IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> workerDefinition)
        where TConfig : notnull
    {
        var mainContract = typeof(IServiceCycleDefinition<TFrame, TConfig, TState, TAction>);
        var mainType = mainDefinition.GetType();
        var additionalMainType =
            (mainDefinition as IServiceCycleAdditionalWorkerForbiddenTypeSource)?.AdditionalWorkerForbiddenType;
        var workerType = workerDefinition.GetType();
        if (ReferenceEquals(mainDefinition, workerDefinition) || mainContract.IsAssignableFrom(workerType))
            throw new InvalidOperationException("Main-thread and worker service definitions must be distinct objects and roles.");

        var visited = new HashSet<Type>();
        ValidateWorkerGraph(
            workerType,
            mainType,
            mainContract,
            additionalMainType,
            visited,
            workerType.Name,
            workerDefinition);
    }

    private static void ValidateWorkerGraph(
        Type type,
        Type mainType,
        Type mainContract,
        Type? additionalMainType,
        HashSet<Type> visited,
        string path,
        object workerDefinition)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (ServiceCycleBoundaryRules.IsExternalRuntimeBoundary(type))
            throw new InvalidOperationException($"Worker definition '{path}' reaches external runtime type '{type.FullName}'.");
        if (mainContract.IsAssignableFrom(type) || mainType.IsAssignableFrom(type) ||
            additionalMainType?.IsAssignableFrom(type) == true)
            throw new InvalidOperationException($"Worker definition '{path}' retains the main-thread service definition.");
        if (type.IsPointer || type.IsByRef || ServiceCycleBoundaryRules.IsHandleOrWeakReference(type))
            throw new InvalidOperationException($"Worker definition '{path}' contains an unsafe runtime handle.");
        if (typeof(Delegate).IsAssignableFrom(type))
            throw new InvalidOperationException($"Worker definition '{path}' contains a delegate that could close over native state.");
        if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(type))
            throw new InvalidOperationException(
                $"Worker definition '{path}' contains open-ended object, interface, array, collection, or memory-view storage.");
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                ValidateWorkerGraph(
                    argument,
                    mainType,
                    mainContract,
                    additionalMainType,
                    visited,
                    path + "<arg>",
                    workerDefinition);
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        var commonAssembly = typeof(ServiceCycleWorkerDefinitionValidator).Assembly;
        if (ServiceCycleStorageShapeRules.IsLeaf(type) || type.Assembly == commonAssembly && type.IsValueType)
            return;
        if (type.Assembly == commonAssembly)
            throw new InvalidOperationException($"Worker definition '{path}' contains mutable Common runtime storage.");
        if (typeNamespace.StartsWith("System", StringComparison.Ordinal))
            throw new InvalidOperationException($"Worker definition '{path}' contains opaque framework reference storage.");
        if (!type.IsValueType && !type.IsSealed)
            throw new InvalidOperationException($"Worker definition '{path}' contains an unsealed reference type.");
        if (!visited.Add(type)) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (IsExactTrustedCommonStorageOwner(current, commonAssembly))
            {
                ValidateAuditedTrustedDependencies(
                    current,
                    mainType,
                    mainContract,
                    additionalMainType,
                    visited,
                    path,
                    workerDefinition);
                continue;
            }
            foreach (var field in current.GetFields(flags))
            {
                if (field.IsStatic)
                {
                    if (field.IsLiteral) continue;
                    throw new InvalidOperationException(
                        $"Worker definition '{path}.{field.Name}' contains non-constant static storage.");
                }
                ValidateWorkerGraph(
                    field.FieldType,
                    mainType,
                    mainContract,
                    additionalMainType,
                    visited,
                    path + "." + field.Name,
                    workerDefinition);
            }
        }
    }

    private static void ValidateAuditedTrustedDependencies(
        Type trustedType,
        Type mainType,
        Type mainContract,
        Type? additionalMainType,
        HashSet<Type> visited,
        string path,
        object workerDefinition)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var field in trustedType.GetFields(flags))
        {
            var audit = field.GetCustomAttribute<ServiceCycleAuditedWorkerDependencyAttribute>(inherit: false);
            if (audit is null)
                continue;
            var dependency = field.GetValue(workerDefinition);
            if (dependency is null)
            {
                if (audit.Required)
                    throw new InvalidOperationException(
                        $"Worker definition '{path}.{field.Name}' has no dependency instance to audit.");
                continue;
            }
            ValidateWorkerGraph(
                dependency.GetType(),
                mainType,
                mainContract,
                additionalMainType,
                visited,
                path + "." + field.Name,
                workerDefinition);
        }
    }

    private static bool IsExactTrustedCommonStorageOwner(Type type, Assembly commonAssembly) =>
        type.Assembly == commonAssembly &&
        type.IsDefined(typeof(ServiceCycleTrustedWorkerStorageAttribute), inherit: false);
}
