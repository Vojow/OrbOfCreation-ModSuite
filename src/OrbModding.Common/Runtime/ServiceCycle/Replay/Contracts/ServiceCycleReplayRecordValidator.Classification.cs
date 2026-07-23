using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

public static partial class ServiceCycleReplayRecordValidator
{
    private static int CompareFields(FieldInfo left, FieldInfo right)
    {
        var nameOrder = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        if (nameOrder != 0) return nameOrder;
        var typeOrder = string.Compare(
            left.FieldType.AssemblyQualifiedName,
            right.FieldType.AssemblyQualifiedName,
            StringComparison.Ordinal);
        return typeOrder != 0 ? typeOrder : left.MetadataToken.CompareTo(right.MetadataToken);
    }

    private static bool IsCollection(Type type)
    {
        if (type.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true) return true;
        foreach (var contract in type.GetInterfaces())
        {
            if (contract.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true) return true;
        }
        return false;
    }

    private static bool IsHandleOrPointer(Type type) =>
        type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsPointer || type.IsByRef ||
        typeof(SafeHandle).IsAssignableFrom(type) ||
        type == typeof(RuntimeTypeHandle) || type == typeof(RuntimeMethodHandle) ||
        type == typeof(RuntimeFieldHandle) || type == typeof(RuntimeArgumentHandle) ||
        type == typeof(TypedReference) || type == typeof(GCHandle);

    private static bool IsAmbient(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
        type == typeof(Random) || type.FullName == "System.Diagnostics.Stopwatch";

    private static bool IsNativeOrRuntime(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        if (assemblyName.StartsWith("Unity", StringComparison.Ordinal) ||
            assemblyName.StartsWith("BepInEx", StringComparison.Ordinal) ||
            assemblyName == "0Harmony" || assemblyName.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
            return true;
        var typeNamespace = type.Namespace ?? string.Empty;
        return typeNamespace.StartsWith("UnityEngine", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("BepInEx", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("HarmonyLib", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("OrbModding.Common.Runtime", StringComparison.Ordinal);
    }

    private static bool IsFrameworkValueType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        var typeNamespace = type.Namespace ?? string.Empty;
        return assemblyName is "mscorlib" or "netstandard" or "System.Private.CoreLib" ||
            assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
            typeNamespace == "System" || typeNamespace.StartsWith("System.", StringComparison.Ordinal);
    }

    private static ServiceCycleReplayRecordValidationResult Reject(
        ServiceCycleReplayRecordViolationCode code,
        Type type,
        int depth,
        ValidationState state) => new(
        code,
        type,
        depth,
        state.FieldOrdinal,
        state.FlattenedScalarCount,
        state.InlineBytes,
        state.LayoutBytes);

    private static ServiceCycleReplayRecordValidationResult Accept(ValidationState state) => new(
        0,
        null,
        0,
        state.FieldOrdinal,
        state.FlattenedScalarCount,
        state.InlineBytes,
        state.LayoutBytes);

    private sealed class ValidationState
    {
        internal int FieldOrdinal;
        internal int FlattenedScalarCount;
        internal int InlineBytes;
        internal int LayoutBytes;
        internal readonly HashSet<Type> Active = new();
    }

    private static class Cache<TRecord> where TRecord : struct, IServiceCycleReplayRecord
    {
        internal static readonly ServiceCycleReplayRecordValidationResult Result = Validate(typeof(TRecord));
    }
}
