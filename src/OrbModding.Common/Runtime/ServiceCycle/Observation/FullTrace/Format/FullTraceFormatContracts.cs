using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

internal readonly struct FullTraceSessionId : IEquatable<FullTraceSessionId>
{
    internal FullTraceSessionId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    internal ulong Value { get; }
    internal bool IsValid => Value != 0;
    public bool Equals(FullTraceSessionId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is FullTraceSessionId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(FullTraceSessionId left, FullTraceSessionId right) => left.Equals(right);
    public static bool operator !=(FullTraceSessionId left, FullTraceSessionId right) => !left.Equals(right);
}

internal enum FullTraceCompleteness : uint
{
    Complete = 1,
    Incomplete = 2,
}

internal enum FullTraceTerminalReason : uint
{
    UserStopped = 1,
    RuntimeShutdown = 2,
    BufferExhausted = 3,
    SequenceExhausted = 4,
    WriteFailed = 5,
    SemanticFault = 6,
}

internal readonly struct FullTraceSegmentDocument
{
    internal FullTraceSegmentDocument(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        ulong ordinal,
        ulong firstTransportSequence,
        int serviceCapacity,
        ServiceCycleSemanticEvent[] events)
    {
        Session = session;
        SemanticSession = semanticSession;
        Ordinal = ordinal;
        FirstTransportSequence = firstTransportSequence;
        ServiceCapacity = serviceCapacity;
        Events = events;
    }

    internal FullTraceSessionId Session { get; }
    internal ServiceCycleTraceSessionId SemanticSession { get; }
    internal ulong Ordinal { get; }
    internal ulong FirstTransportSequence { get; }
    internal int ServiceCapacity { get; }
    internal ServiceCycleSemanticEvent[] Events { get; }
}

internal readonly struct FullTraceManifestDocument
{
    internal FullTraceManifestDocument(
        FullTraceCompleteness completeness,
        FullTraceTerminalReason reason,
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        int serviceCapacity,
        ulong segmentCount,
        ulong firstSemanticSequence,
        ulong acceptedRecords,
        ulong writtenRecords,
        ulong firstIncompleteTransportSequence,
        ulong firstIncompleteSemanticSequence,
        long firstTimestampTicks,
        long lastTimestampTicks,
        ulong segmentBytes)
    {
        Completeness = completeness;
        Reason = reason;
        Session = session;
        SemanticSession = semanticSession;
        ServiceCapacity = serviceCapacity;
        SegmentCount = segmentCount;
        FirstSemanticSequence = firstSemanticSequence;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        FirstIncompleteTransportSequence = firstIncompleteTransportSequence;
        FirstIncompleteSemanticSequence = firstIncompleteSemanticSequence;
        FirstTimestampTicks = firstTimestampTicks;
        LastTimestampTicks = lastTimestampTicks;
        SegmentBytes = segmentBytes;
    }

    internal FullTraceCompleteness Completeness { get; }
    internal FullTraceTerminalReason Reason { get; }
    internal FullTraceSessionId Session { get; }
    internal ServiceCycleTraceSessionId SemanticSession { get; }
    internal int ServiceCapacity { get; }
    internal ulong SegmentCount { get; }
    internal ulong FirstSemanticSequence { get; }
    internal ulong AcceptedRecords { get; }
    internal ulong WrittenRecords { get; }
    internal ulong FirstIncompleteTransportSequence { get; }
    internal ulong FirstIncompleteSemanticSequence { get; }
    internal long FirstTimestampTicks { get; }
    internal long LastTimestampTicks { get; }
    internal ulong SegmentBytes { get; }
}
