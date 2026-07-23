using System;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

public static partial class ServiceCycleReplayRecordValidator
{
    /// <summary>
    /// Computes a platform-independent upper bound for the CLR inline storage of an already validated
    /// sequential value graph. Every allowed scalar is at most 16 bytes and cannot require alignment
    /// greater than its size. Using that full alignment for every field can overestimate a runtime's
    /// actual layout, but it cannot hide padding the way a flattened scalar sum or marshaling layout can.
    /// </summary>
    private static int CalculateManagedLayoutUpperBound(
        Type type,
        IDictionary<Type, LayoutBound> completed)
    {
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (AllowedScalars.Contains(type)) return ScalarBytes(type);
        if (completed.TryGetValue(type, out var cached)) return cached.Size;

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(fields, CompareLayoutFields);
        var offset = 0;
        var maximumAlignment = 1;
        foreach (var field in fields)
        {
            var fieldType = field.FieldType.IsEnum
                ? Enum.GetUnderlyingType(field.FieldType)
                : field.FieldType;
            LayoutBound fieldBound;
            if (AllowedScalars.Contains(fieldType))
            {
                var scalarBytes = ScalarBytes(fieldType);
                fieldBound = new LayoutBound(scalarBytes, scalarBytes);
            }
            else
            {
                var nestedSize = CalculateManagedLayoutUpperBound(fieldType, completed);
                fieldBound = completed[fieldType];
                if (fieldBound.Size != nestedSize)
                    throw new InvalidOperationException("The replay layout cache is inconsistent.");
            }

            offset = AlignChecked(offset, fieldBound.Alignment);
            offset = checked(offset + fieldBound.Size);
            maximumAlignment = Math.Max(maximumAlignment, fieldBound.Alignment);
        }

        var result = new LayoutBound(AlignChecked(offset, maximumAlignment), maximumAlignment);
        completed.Add(type, result);
        return result.Size;
    }

    private static ServiceCycleReplayRecordValidationResult AccumulateScalar(
        Type scalar,
        int depth,
        ValidationState state)
    {
        if (state.FlattenedScalarCount >= MaximumFlattenedScalarCount)
            return Reject(
                ServiceCycleReplayRecordViolationCode.MaximumFlattenedScalarCountExceeded,
                scalar,
                depth,
                state);
        state.FlattenedScalarCount++;
        var bytes = ScalarBytes(scalar);
        if (state.InlineBytes > MaximumInlineBytes - bytes)
            return Reject(ServiceCycleReplayRecordViolationCode.MaximumInlineBytesExceeded, scalar, depth, state);
        state.InlineBytes += bytes;
        return Accept(state);
    }

    private static int ScalarBytes(Type scalar)
    {
        if (scalar == typeof(bool) || scalar == typeof(byte) || scalar == typeof(sbyte)) return 1;
        if (scalar == typeof(short) || scalar == typeof(ushort) || scalar == typeof(char)) return 2;
        if (scalar == typeof(int) || scalar == typeof(uint) || scalar == typeof(float)) return 4;
        if (scalar == typeof(long) || scalar == typeof(ulong) || scalar == typeof(double)) return 8;
        if (scalar == typeof(decimal)) return 16;
        throw new InvalidOperationException("The scalar allowlist and size table disagree.");
    }

    private static int AlignChecked(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static int CompareLayoutFields(FieldInfo left, FieldInfo right) =>
        left.MetadataToken.CompareTo(right.MetadataToken);

    private readonly struct LayoutBound
    {
        internal LayoutBound(int size, int alignment)
        {
            Size = size;
            Alignment = alignment;
        }

        internal int Size { get; }
        internal int Alignment { get; }
    }
}
