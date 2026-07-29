using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal static class ServiceCycleStorageShapeRules
{
    internal static bool IsImmutablePublication(ServiceCycleTypeRole role) =>
        role is ServiceCycleTypeRole.Configuration or ServiceCycleTypeRole.Action or
            ServiceCycleTypeRole.Strategy or ServiceCycleTypeRole.World;

    internal static bool IsLeaf(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan);

    internal static bool IsUnsafeMutableExposure(Type type) =>
        type == typeof(object) ||
        type.IsInterface ||
        type.IsArray ||
        IsBackingMemoryView(type) ||
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    internal static bool IsReadonlyValue(Type type) =>
        !type.IsValueType || type.IsDefined(typeof(IsReadOnlyAttribute), false);

    internal static bool IsPublicOrProtected(MethodInfo method) =>
        method.IsPublic ||
        method.IsFamily ||
        method.IsFamilyOrAssembly ||
        method.IsFamilyAndAssembly;

    internal static bool IsPublicOrProtected(ConstructorInfo constructor) =>
        constructor.IsPublic ||
        constructor.IsFamily ||
        constructor.IsFamilyOrAssembly ||
        constructor.IsFamilyAndAssembly;

    internal static bool ShouldAuditDeclaredMethods(Type type) =>
        type.Assembly != typeof(object).Assembly;

    internal static bool IsHarmlessObjectContract(MethodInfo method)
    {
        if (method.GetBaseDefinition().DeclaringType != typeof(object)) return false;
        var parameters = method.GetParameters();
        return method.Name == nameof(ToString) &&
                method.ReturnType == typeof(string) &&
                parameters.Length == 0 ||
            method.Name == nameof(GetHashCode) &&
                method.ReturnType == typeof(int) &&
                parameters.Length == 0 ||
            method.Name == nameof(Equals) &&
                method.ReturnType == typeof(bool) &&
                parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(object);
    }

    private static bool IsBackingMemoryView(Type type)
    {
        if (!type.IsGenericType) return false;
        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Span<>) ||
            definition == typeof(ReadOnlySpan<>) ||
            definition == typeof(Memory<>) ||
            definition == typeof(ReadOnlyMemory<>);
    }
}
