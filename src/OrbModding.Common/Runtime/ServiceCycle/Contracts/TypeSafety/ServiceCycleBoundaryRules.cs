using System;
using System.Runtime.InteropServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal static class ServiceCycleBoundaryRules
{
    internal static bool IsExternalRuntimeBoundary(Type type)
    {
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        if (assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "BepInEx" or "0Harmony" ||
            assembly.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            return true;
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        return typeNamespace.StartsWith("UnityEngine", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("BepInEx", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("HarmonyLib", StringComparison.Ordinal);
    }

    internal static bool IsHandleOrWeakReference(Type type)
    {
        if (type == typeof(IntPtr) ||
            type == typeof(UIntPtr) ||
            type == typeof(GCHandle) ||
            type == typeof(WeakReference) ||
            typeof(SafeHandle).IsAssignableFrom(type) ||
            typeof(CriticalHandle).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>);
    }
}
