using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

/// <summary>
/// Proves only the detached storage shape: a finite readonly value graph made from reviewed scalar
/// primitives, enums, and explicitly opted-in feature value records. It performs no serialization and
/// treats every reflection uncertainty as a rejection.
/// </summary>
public static partial class ServiceCycleReplayRecordValidator
{
    public const int MaximumDepth = 16;
    public const int MaximumFlattenedScalarCount = 256;
    public const int MaximumInlineBytes = 2_048;

    private static readonly HashSet<Type> AllowedScalars = new()
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(char),
        typeof(float), typeof(double), typeof(decimal),
    };

    public static ServiceCycleReplayRecordValidationResult Validate<TRecord>()
        where TRecord : struct, IServiceCycleReplayRecord => Cache<TRecord>.Result;

    public static ServiceCycleReplayRecordValidationResult Validate(Type recordType)
    {
        if (recordType is null) throw new ArgumentNullException(nameof(recordType));
        try
        {
            var state = new ValidationState();
            return ValidateType(recordType, true, 0, state);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not StackOverflowException)
        {
            return Reject(ServiceCycleReplayRecordViolationCode.ReflectionFailure, recordType, 0, new ValidationState());
        }
    }

    public static void EnsureValid<TRecord>() where TRecord : struct, IServiceCycleReplayRecord
    {
        var result = Validate<TRecord>();
        if (!result.IsValid)
            throw new InvalidOperationException(
                $"Detached replay record type {typeof(TRecord).FullName} was rejected with stable code " +
                $"{(int)result.Code} ({result.Code}) at field {result.FieldOrdinal}, depth {result.Depth}.");
    }

    private static ServiceCycleReplayRecordValidationResult ValidateType(
        Type type,
        bool root,
        int depth,
        ValidationState state)
    {
        if (depth > MaximumDepth)
            return Reject(ServiceCycleReplayRecordViolationCode.MaximumDepthExceeded, type, depth, state);
        if (IsAmbient(type))
            return Reject(ServiceCycleReplayRecordViolationCode.AmbientSource, type, depth, state);
        if (IsNativeOrRuntime(type))
            return Reject(ServiceCycleReplayRecordViolationCode.NativeOrRuntimeType, type, depth, state);
        if (type == typeof(string))
            return Reject(ServiceCycleReplayRecordViolationCode.String, type, depth, state);
        if (type == typeof(object))
            return Reject(ServiceCycleReplayRecordViolationCode.Object, type, depth, state);
        if (type.IsInterface)
            return Reject(ServiceCycleReplayRecordViolationCode.Interface, type, depth, state);
        if (typeof(Delegate).IsAssignableFrom(type))
            return Reject(ServiceCycleReplayRecordViolationCode.Delegate, type, depth, state);
        if (type.IsArray || IsCollection(type))
            return Reject(ServiceCycleReplayRecordViolationCode.ArrayOrCollection, type, depth, state);
        if (IsHandleOrPointer(type))
            return Reject(ServiceCycleReplayRecordViolationCode.HandleOrPointer, type, depth, state);
        if (Nullable.GetUnderlyingType(type) is not null)
            return Reject(ServiceCycleReplayRecordViolationCode.Nullable, type, depth, state);
        if (type.IsByRefLike)
            return Reject(ServiceCycleReplayRecordViolationCode.ByRefLike, type, depth, state);
        if (type.IsGenericType || type.ContainsGenericParameters || type.IsGenericParameter)
            return Reject(ServiceCycleReplayRecordViolationCode.OpenOrConstructedGeneric, type, depth, state);
        if (!type.IsValueType)
            return Reject(ServiceCycleReplayRecordViolationCode.ReferenceType, type, depth, state);

        if (AllowedScalars.Contains(type))
        {
            if (root)
                return Reject(ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord, type, depth, state);
            return AccumulateScalar(type, depth, state);
        }
        if (type.IsPrimitive)
            return Reject(ServiceCycleReplayRecordViolationCode.UnsupportedPrimitive, type, depth, state);
        if (type.IsEnum)
        {
            if (root)
                return Reject(ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord, type, depth, state);
            var underlying = Enum.GetUnderlyingType(type);
            return AllowedScalars.Contains(underlying)
                ? AccumulateScalar(underlying, depth, state)
                : Reject(ServiceCycleReplayRecordViolationCode.UnsupportedPrimitive, type, depth, state);
        }
        if (IsFrameworkValueType(type))
            return Reject(ServiceCycleReplayRecordViolationCode.UnreviewedFrameworkValueType, type, depth, state);
        if (!typeof(IServiceCycleReplayRecord).IsAssignableFrom(type))
            return Reject(ServiceCycleReplayRecordViolationCode.MissingReplayRecordMarker, type, depth, state);
        var staticFields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(staticFields, CompareFields);
        foreach (var field in staticFields)
        {
            if (field.IsLiteral) continue;
            state.FieldOrdinal = checked(state.FieldOrdinal + 1);
            state.Active.Remove(type);
            return Reject(ServiceCycleReplayRecordViolationCode.StaticStorage, field.FieldType, depth, state);
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(fields, CompareFields);
        if (fields.Length == 0)
            return Reject(ServiceCycleReplayRecordViolationCode.EmptyValueRecord, type, depth, state);

        var layout = type.StructLayoutAttribute;
        if (!type.IsLayoutSequential ||
            type.IsExplicitLayout ||
            layout is null ||
            layout.Size != 0)
            return Reject(ServiceCycleReplayRecordViolationCode.ExplicitOrUnmanagedLayout, type, depth, state);
        if (!type.IsDefined(typeof(IsReadOnlyAttribute), false))
            return Reject(root
                ? ServiceCycleReplayRecordViolationCode.RootMustBeReadonlyRecord
                : ServiceCycleReplayRecordViolationCode.MutableValueType, type, depth, state);
        if (!state.Active.Add(type))
            return Reject(ServiceCycleReplayRecordViolationCode.TypeGraphCycle, type, depth, state);

        foreach (var field in fields)
        {
            state.FieldOrdinal = checked(state.FieldOrdinal + 1);
            if (!field.IsInitOnly)
            {
                state.Active.Remove(type);
                return Reject(ServiceCycleReplayRecordViolationCode.MutableValueType, field.FieldType, depth + 1, state);
            }
            if (field.IsDefined(typeof(FixedBufferAttribute), false) ||
                field.IsDefined(typeof(MarshalAsAttribute), false))
            {
                state.Active.Remove(type);
                return Reject(
                    ServiceCycleReplayRecordViolationCode.ExplicitOrUnmanagedLayout,
                    field.FieldType,
                    depth + 1,
                    state);
            }
            var result = ValidateType(field.FieldType, false, depth + 1, state);
            if (!result.IsValid)
            {
                state.Active.Remove(type);
                return result;
            }
        }

        if (root)
        {
            try
            {
                state.LayoutBytes = CalculateManagedLayoutUpperBound(type, new Dictionary<Type, LayoutBound>());
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException && exception is not StackOverflowException)
            {
                state.Active.Remove(type);
                return Reject(ServiceCycleReplayRecordViolationCode.ReflectionFailure, type, depth, state);
            }
            if (state.LayoutBytes > MaximumInlineBytes)
            {
                state.Active.Remove(type);
                return Reject(ServiceCycleReplayRecordViolationCode.MaximumInlineBytesExceeded, type, depth, state);
            }
        }

        state.Active.Remove(type);
        return Accept(state);
    }

}
