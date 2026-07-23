using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal enum FullTraceRuntimeSessionState
{
    Idle = 0,
    Arming = 1,
    Recording = 2,
    Stopping = 3,
    Complete = 4,
    Incomplete = 5,
}

internal readonly struct FullTraceRuntimeSessionSnapshot
{
    internal FullTraceRuntimeSessionSnapshot(
        FullTraceRuntimeSessionState state,
        long acceptedRecords,
        long writtenRecords,
        long bytesWritten,
        long segmentCount,
        long firstIncompleteSequence,
        bool manifestCommitted,
        FullTraceTerminalReason terminalReason,
        BufferedSegmentFaultReason faultReason)
    {
        State = state;
        AcceptedRecords = acceptedRecords;
        WrittenRecords = writtenRecords;
        BytesWritten = bytesWritten;
        SegmentCount = segmentCount;
        FirstIncompleteSequence = firstIncompleteSequence;
        ManifestCommitted = manifestCommitted;
        TerminalReason = terminalReason;
        FaultReason = faultReason;
    }

    internal FullTraceRuntimeSessionState State { get; }
    internal long AcceptedRecords { get; }
    internal long WrittenRecords { get; }
    internal long BytesWritten { get; }
    internal long SegmentCount { get; }
    internal long FirstIncompleteSequence { get; }
    internal bool ManifestCommitted { get; }
    internal FullTraceTerminalReason TerminalReason { get; }
    internal BufferedSegmentFaultReason FaultReason { get; }
}
