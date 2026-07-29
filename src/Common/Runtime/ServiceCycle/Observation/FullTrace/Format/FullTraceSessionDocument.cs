using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

internal enum FullTraceSessionState
{
    Complete,
    Incomplete,
    Interrupted,
}

internal readonly struct FullTraceSessionDocument
{
    internal FullTraceSessionDocument(
        FullTraceSessionState state,
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
        ulong segmentBytes,
        FullTraceTerminalReason? terminalReason)
    {
        State = state;
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
        TerminalReason = terminalReason;
    }

    internal FullTraceSessionState State { get; }
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
    internal FullTraceTerminalReason? TerminalReason { get; }
    internal bool HasTerminalEvidence => State != FullTraceSessionState.Interrupted;
}

internal interface IFullTracePriorEventReader
{
    ServiceCycleSemanticEvent ReadEvent(ulong segmentOrdinal, int eventIndex);
}
