using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal static class ServiceCycleTypeGraphWalker
{
    internal static ServiceCycleTypeViolation? Validate(
        Type type,
        ServiceCycleTypeRole role,
        Type frameType,
        string path) =>
        FindViolation(type, role, frameType, true, new HashSet<Type>(), path);

    private static ServiceCycleTypeViolation? FindViolation(
        Type type,
        ServiceCycleTypeRole role,
        Type frameType,
        bool isRoot,
        ISet<Type> visited,
        string path)
    {
        if (role is ServiceCycleTypeRole.State or ServiceCycleTypeRole.Action && type == frameType)
            return Violation(path, type, role == ServiceCycleTypeRole.State
                ? "state cannot retain its service frame"
                : "actions cannot retain their service frame");
        if (ServiceCycleBoundaryRules.IsExternalRuntimeBoundary(type))
            return Violation(path, type, "external game, loader, or patching runtime boundary");
        if (ServiceCycleBoundaryRules.IsHandleOrWeakReference(type))
            return Violation(path, type, "native handles, pointer-sized handles, and weak references are not service-cycle storage");
        if (type == typeof(object) || type.IsInterface || type.IsPointer || type.IsByRef || type.IsByRefLike)
            return Violation(path, type, "open-ended object, interface, pointer, or by-ref-like storage");
        if (typeof(Delegate).IsAssignableFrom(type))
            return Violation(path, type, "delegate storage");
        if (ServiceCycleStorageShapeRules.IsLeaf(type)) return null;

        var immutable = ServiceCycleStorageShapeRules.IsImmutablePublication(role);
        if (type.IsArray)
        {
            if (immutable || isRoot)
                return Violation(path, type, "arrays are not immutable publication values or root frame/state values");
            return FindViolation(type.GetElementType()!, role, frameType, false, visited, path + "[]");
        }
        if (typeof(IEnumerable).IsAssignableFrom(type))
            return Violation(path, type, "collection storage is not an audited publication shape");

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return FindViolation(nullable, role, frameType, false, visited, path + ".Value");

        if (type.IsGenericType && !type.ContainsGenericParameters)
        {
            var arguments = type.GetGenericArguments();
            for (var index = 0; index < arguments.Length; index++)
            {
                var violation = FindViolation(
                    arguments[index], role, frameType, false, visited, path + $"<argument:{index}>");
                if (violation.HasValue) return violation;
            }
        }

        if (!type.IsValueType && !type.IsSealed)
            return Violation(path, type, "service-cycle reference storage must be sealed against unsafe runtime subtypes");
        if (immutable && !ServiceCycleStorageShapeRules.IsReadonlyValue(type))
            return Violation(path, type, "published value structs must be readonly");

        if (!visited.Add(type)) return null;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var violation = AuditFields(current, role, frameType, immutable, visited, path);
            if (violation.HasValue) return violation;
            violation = AuditProperties(current, role, frameType, immutable, visited, path);
            if (violation.HasValue) return violation;
            violation = AuditMethods(current, role, frameType, visited, path);
            if (violation.HasValue) return violation;
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditFields(
        Type current,
        ServiceCycleTypeRole role,
        Type frameType,
        bool immutable,
        ISet<Type> visited,
        string path)
    {
        foreach (var field in current.GetFields(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (field.IsStatic)
            {
                if (field.IsLiteral) continue;
                return Violation(path + "." + field.Name, field.FieldType,
                    "service-cycle data shapes cannot own non-constant static storage");
            }
            if (immutable && !field.IsInitOnly)
                return Violation(path + "." + field.Name, field.FieldType, "published fields must be readonly");
            if (!immutable && (field.IsPublic || field.IsFamily) &&
                ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(field.FieldType))
            {
                return Violation(path + "." + field.Name, field.FieldType,
                    "mutable backing storage cannot be publicly exposed");
            }

            var violation = FindViolation(
                field.FieldType, role, frameType, false, visited, path + "." + field.Name);
            if (violation.HasValue) return violation;
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditProperties(
        Type current,
        ServiceCycleTypeRole role,
        Type frameType,
        bool immutable,
        ISet<Type> visited,
        string path)
    {
        foreach (var property in current.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            var getter = property.GetMethod;
            if (getter is not null && ServiceCycleStorageShapeRules.IsPublicOrProtected(getter))
            {
                if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(property.PropertyType))
                    return Violation(path + "." + property.Name, property.PropertyType,
                        "service-cycle properties cannot expose arrays, collections, memory views, object, or interface surfaces");
                var violation = FindViolation(
                    property.PropertyType, role, frameType, false, visited, path + "." + property.Name);
                if (violation.HasValue) return violation;
            }

            var setter = property.SetMethod;
            if (setter is not null && ServiceCycleStorageShapeRules.IsPublicOrProtected(setter) &&
                (immutable || role is ServiceCycleTypeRole.Frame or ServiceCycleTypeRole.State))
            {
                return Violation(path + "." + property.Name, property.PropertyType,
                    "service-cycle publication and frame/state surfaces cannot expose public or protected setters");
            }
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditMethods(
        Type current,
        ServiceCycleTypeRole role,
        Type frameType,
        ISet<Type> visited,
        string path)
    {
        if (!ServiceCycleStorageShapeRules.ShouldAuditDeclaredMethods(current)) return null;
        foreach (var method in current.GetMethods(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!ServiceCycleStorageShapeRules.IsPublicOrProtected(method) ||
                ServiceCycleStorageShapeRules.IsHarmlessObjectContract(method))
            {
                continue;
            }
            if (method.IsGenericMethodDefinition)
                return Violation(path + "." + method.Name, current, "open generic method surfaces are not permitted");
            var violation = FindMethodSurfaceViolation(
                method.ReturnType, role, frameType, visited, path + "." + method.Name + " return");
            if (violation.HasValue) return violation;
            foreach (var parameter in method.GetParameters())
            {
                violation = FindMethodSurfaceViolation(
                    parameter.ParameterType, role, frameType, visited,
                    path + "." + method.Name + " parameter " + parameter.Name);
                if (violation.HasValue) return violation;
            }
        }

        foreach (var constructor in current.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!ServiceCycleStorageShapeRules.IsPublicOrProtected(constructor)) continue;
            foreach (var parameter in constructor.GetParameters())
            {
                var violation = FindMethodSurfaceViolation(
                    parameter.ParameterType, role, frameType, visited, path + ".ctor parameter " + parameter.Name);
                if (violation.HasValue) return violation;
            }
        }

        return null;
    }

    private static ServiceCycleTypeViolation? FindMethodSurfaceViolation(
        Type type,
        ServiceCycleTypeRole role,
        Type frameType,
        ISet<Type> visited,
        string path)
    {
        if (type == typeof(void)) return null;
        if (type.IsGenericParameter || type.ContainsGenericParameters)
            return Violation(path, type, "open generic method surfaces are not permitted");
        if (type.IsByRef || type.IsPointer || type.IsByRefLike)
            return Violation(path, type, "by-ref, pointer, and by-ref-like method surfaces are not permitted");
        if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(type))
            return Violation(path, type,
                "open-ended object, interface, array, collection, and memory-view method surfaces are not permitted");
        return FindViolation(type, role, frameType, false, visited, path);
    }

    private static ServiceCycleTypeViolation Violation(string path, Type type, string reason) =>
        new(path, type, reason);
}
