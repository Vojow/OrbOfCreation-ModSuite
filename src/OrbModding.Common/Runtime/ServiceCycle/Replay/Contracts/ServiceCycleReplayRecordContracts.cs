using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

/// <summary>
/// Stable roles for the detached records that make one service cycle replayable.
/// Values are persisted and must not be renumbered.
/// </summary>
public enum ServiceCycleReplayRecordKind
{
    CycleInput = 1,
    PreviousState = 2,
    NextState = 3,
    Action = 4,
}

/// <summary>
/// Explicit opt-in identity for a detached replay value record. This marker carries no data; every
/// root and nested non-scalar record in a replay graph must implement it.
/// </summary>
public interface IServiceCycleReplayRecord
{
}

/// <summary>
/// Identifies one detached record without retaining a live frame, state, or action.
/// </summary>
public readonly struct ServiceCycleReplayRecordIdentity : IEquatable<ServiceCycleReplayRecordIdentity>
{
    public ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind kind, int index)
    {
        if (kind is < ServiceCycleReplayRecordKind.CycleInput or > ServiceCycleReplayRecordKind.Action)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (kind != ServiceCycleReplayRecordKind.Action && index != 0)
            throw new ArgumentException("Only action records may have a nonzero index.", nameof(index));
        Kind = kind;
        Index = index;
    }

    public ServiceCycleReplayRecordKind Kind { get; }
    public int Index { get; }
    public bool IsValid =>
        Kind is >= ServiceCycleReplayRecordKind.CycleInput and <= ServiceCycleReplayRecordKind.Action &&
        Index >= 0 && (Kind == ServiceCycleReplayRecordKind.Action || Index == 0);

    public bool Equals(ServiceCycleReplayRecordIdentity other) => Kind == other.Kind && Index == other.Index;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayRecordIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Kind, Index);
    public static bool operator ==(ServiceCycleReplayRecordIdentity left, ServiceCycleReplayRecordIdentity right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayRecordIdentity left, ServiceCycleReplayRecordIdentity right) =>
        !left.Equals(right);
}

/// <summary>
/// Version and strict per-record byte bound promised by a feature codec.
/// </summary>
public readonly struct ServiceCycleReplayCodecDescriptor : IEquatable<ServiceCycleReplayCodecDescriptor>
{
    public ServiceCycleReplayCodecDescriptor(int schemaVersion, int maximumEncodedBytes)
    {
        if (schemaVersion is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (maximumEncodedBytes is <= 0 or > ServiceCycleReplayCodecLimits.MaximumEncodedBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        SchemaVersion = (ushort)schemaVersion;
        MaximumEncodedBytes = maximumEncodedBytes;
    }

    public ushort SchemaVersion { get; }
    public int MaximumEncodedBytes { get; }
    public bool IsValid => SchemaVersion != 0 &&
        MaximumEncodedBytes is > 0 and <= ServiceCycleReplayCodecLimits.MaximumEncodedBytes;
    public bool Equals(ServiceCycleReplayCodecDescriptor other) =>
        SchemaVersion == other.SchemaVersion && MaximumEncodedBytes == other.MaximumEncodedBytes;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayCodecDescriptor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SchemaVersion, MaximumEncodedBytes);
    public static bool operator ==(ServiceCycleReplayCodecDescriptor left, ServiceCycleReplayCodecDescriptor right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayCodecDescriptor left, ServiceCycleReplayCodecDescriptor right) =>
        !left.Equals(right);
}

public static class ServiceCycleReplayCodecLimits
{
    public const int MaximumEncodedBytes = 1_048_576;
}

/// <summary>
/// Feature-owned, explicitly versioned codec for one detached value record. Implementations must be
/// finite, deterministic, canonical, and free of I/O. Common validates the descriptor, type graph,
/// destination capacity, and returned byte count around every invocation.
/// </summary>
public interface IServiceCycleReplayCodec<TRecord> where TRecord : struct, IServiceCycleReplayRecord
{
    ServiceCycleReplayCodecDescriptor Descriptor { get; }
    int Encode(in TRecord record, Span<byte> destination);
    TRecord Decode(ReadOnlySpan<byte> source);
}

/// <summary>
/// Feature-owned semantic comparison for decoded detached value records. A positive field code is a
/// stable, feature-defined schema identity; it is not a display string or reflection path.
/// </summary>
public interface IServiceCycleReplayComparer<TRecord> where TRecord : struct, IServiceCycleReplayRecord
{
    ServiceCycleReplayRecordComparison Compare(in TRecord expected, in TRecord actual);
}

public readonly struct ServiceCycleReplayRecordComparison : IEquatable<ServiceCycleReplayRecordComparison>
{
    public ServiceCycleReplayRecordComparison(int fieldCode, int elementIndex = 0)
    {
        if (fieldCode <= 0) throw new ArgumentOutOfRangeException(nameof(fieldCode));
        if (elementIndex < 0) throw new ArgumentOutOfRangeException(nameof(elementIndex));
        FieldCode = fieldCode;
        ElementIndex = elementIndex;
    }

    public static ServiceCycleReplayRecordComparison Match => default;
    public int FieldCode { get; }
    public int ElementIndex { get; }
    public bool IsMatch => FieldCode == 0 && ElementIndex == 0;
    public bool IsValid => IsMatch || FieldCode > 0 && ElementIndex >= 0;
    public bool Equals(ServiceCycleReplayRecordComparison other) =>
        FieldCode == other.FieldCode && ElementIndex == other.ElementIndex;
    public override bool Equals(object? obj) => obj is ServiceCycleReplayRecordComparison other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(FieldCode, ElementIndex);
    public static bool operator ==(ServiceCycleReplayRecordComparison left, ServiceCycleReplayRecordComparison right) =>
        left.Equals(right);
    public static bool operator !=(ServiceCycleReplayRecordComparison left, ServiceCycleReplayRecordComparison right) =>
        !left.Equals(right);
}

/// <summary>
/// Stable fail-closed checks around a codec invocation. Values are persisted and must not be renumbered.
/// </summary>
public enum ServiceCycleReplayCodecContractCode
{
    Valid = 1,
    InvalidDescriptor = 2,
    DestinationBelowPromisedCapacity = 3,
    NegativeEncodedLength = 4,
    EncodedLengthExceedsBound = 5,
    EncodedLengthExceedsDestination = 6,
    SourceExceedsBound = 7,
}

public static class ServiceCycleReplayCodecContract
{
    public static ServiceCycleReplayCodecContractCode ValidateDescriptor(
        in ServiceCycleReplayCodecDescriptor descriptor) =>
        descriptor.IsValid
            ? ServiceCycleReplayCodecContractCode.Valid
            : ServiceCycleReplayCodecContractCode.InvalidDescriptor;

    public static ServiceCycleReplayCodecContractCode ValidateEncodeResult(
        in ServiceCycleReplayCodecDescriptor descriptor,
        int destinationCapacity,
        int encodedLength)
    {
        if (!descriptor.IsValid) return ServiceCycleReplayCodecContractCode.InvalidDescriptor;
        if (encodedLength < 0) return ServiceCycleReplayCodecContractCode.NegativeEncodedLength;
        if (encodedLength > descriptor.MaximumEncodedBytes)
            return ServiceCycleReplayCodecContractCode.EncodedLengthExceedsBound;
        if (encodedLength > destinationCapacity)
            return ServiceCycleReplayCodecContractCode.EncodedLengthExceedsDestination;
        if (destinationCapacity < descriptor.MaximumEncodedBytes)
            return ServiceCycleReplayCodecContractCode.DestinationBelowPromisedCapacity;
        return ServiceCycleReplayCodecContractCode.Valid;
    }

    public static ServiceCycleReplayCodecContractCode ValidateDecodeSource(
        in ServiceCycleReplayCodecDescriptor descriptor,
        int sourceLength)
    {
        if (!descriptor.IsValid) return ServiceCycleReplayCodecContractCode.InvalidDescriptor;
        if (sourceLength < 0 || sourceLength > descriptor.MaximumEncodedBytes)
            return ServiceCycleReplayCodecContractCode.SourceExceedsBound;
        return ServiceCycleReplayCodecContractCode.Valid;
    }
}
