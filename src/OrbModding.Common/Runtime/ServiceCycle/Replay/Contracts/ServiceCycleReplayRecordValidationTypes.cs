using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

/// <summary>Stable reasons a detached replay record type is rejected. Values must not be renumbered.</summary>
public enum ServiceCycleReplayRecordViolationCode
{
    RootMustBeReadonlyRecord = 1,
    ReferenceType = 2,
    String = 3,
    Object = 4,
    Interface = 5,
    Delegate = 6,
    ArrayOrCollection = 7,
    HandleOrPointer = 8,
    Nullable = 9,
    OpenOrConstructedGeneric = 10,
    ByRefLike = 11,
    MutableValueType = 12,
    NativeOrRuntimeType = 13,
    AmbientSource = 14,
    UnsupportedPrimitive = 15,
    ExplicitOrUnmanagedLayout = 16,
    TypeGraphCycle = 17,
    MaximumDepthExceeded = 18,
    MaximumFlattenedScalarCountExceeded = 19,
    ReflectionFailure = 20,
    UnreviewedFrameworkValueType = 21,
    StaticStorage = 22,
    EmptyValueRecord = 23,
    MissingReplayRecordMarker = 24,
    MaximumInlineBytesExceeded = 25,
}

public readonly struct ServiceCycleReplayRecordValidationResult
{
    internal ServiceCycleReplayRecordValidationResult(
        ServiceCycleReplayRecordViolationCode code,
        Type? rejectedType,
        int depth,
        int fieldOrdinal,
        int flattenedScalarCount,
        int inlineBytes,
        int layoutBytes)
    {
        Code = code;
        RejectedType = rejectedType;
        Depth = depth;
        FieldOrdinal = fieldOrdinal;
        FlattenedScalarCount = flattenedScalarCount;
        InlineBytes = inlineBytes;
        LayoutBytes = layoutBytes;
    }

    public ServiceCycleReplayRecordViolationCode Code { get; }
    public Type? RejectedType { get; }
    public int Depth { get; }
    public int FieldOrdinal { get; }
    public int FlattenedScalarCount { get; }
    public int InlineBytes { get; }
    public int LayoutBytes { get; }
    public bool IsValid => Code == 0;
}
