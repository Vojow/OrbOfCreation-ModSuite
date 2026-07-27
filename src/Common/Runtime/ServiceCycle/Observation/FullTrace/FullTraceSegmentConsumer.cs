using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal sealed class FullTraceSegmentConsumer : IBufferedSegmentConsumer<ServiceCycleSemanticEvent>
{
    private readonly ISegmentSessionStorage _storage;
    private readonly FullTraceTerminalRequest _terminalRequest;
    private readonly FullTraceSessionId _session;
    private readonly ServiceCycleTraceSessionId _semanticSession;
    private readonly int _serviceCapacity;
    private readonly ulong _firstSemanticSequence;
    private byte[]? _encodingBuffer;
    private ulong _nextTransportSequence = 1;
    private ulong _nextSemanticSequence;
    private ulong _segmentCount;
    private ulong _writtenRecords;
    private ulong _segmentBytes;
    private long _firstTimestampTicks;
    private long _lastTimestampTicks;
    private bool _partialSegmentWritten;
    private int _manifestCommitted;
    private int _terminalReason;

    internal FullTraceSegmentConsumer(
        ISegmentSessionStorage storage,
        FullTraceTerminalRequest terminalRequest,
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        int serviceCapacity,
        ulong firstSemanticSequence = 1)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _terminalRequest = terminalRequest ?? throw new ArgumentNullException(nameof(terminalRequest));
        if (!session.IsValid) throw new ArgumentException("A valid full-trace session is required.", nameof(session));
        if (!semanticSession.IsValid)
            throw new ArgumentException("A valid semantic session is required.", nameof(semanticSession));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        if (firstSemanticSequence == 0) throw new ArgumentOutOfRangeException(nameof(firstSemanticSequence));
        _session = session;
        _semanticSession = semanticSession;
        _serviceCapacity = serviceCapacity;
        _firstSemanticSequence = firstSemanticSequence;
        _nextSemanticSequence = firstSemanticSequence;
    }

    internal bool ManifestCommitted => Volatile.Read(ref _manifestCommitted) != 0;
    internal FullTraceTerminalReason TerminalReason =>
        (FullTraceTerminalReason)Volatile.Read(ref _terminalReason);

    public void Initialize()
    {
        _storage.Initialize();
        _encodingBuffer = new byte[FullTraceSegmentCodec.GetEncodedLength(FullTraceSegmentCodec.MaximumRecords)];
    }

    public int Write(long blockOrdinal, long firstRecordSequence, ReadOnlySpan<ServiceCycleSemanticEvent> records)
    {
        var buffer = _encodingBuffer ?? throw new InvalidOperationException("The full-trace consumer is not initialized.");
        if (blockOrdinal < 0 || checked((ulong)blockOrdinal) != _segmentCount)
            throw new InvalidOperationException("Full-trace segment ordinals must be dense.");
        if (firstRecordSequence <= 0 || checked((ulong)firstRecordSequence) != _nextTransportSequence)
            throw new InvalidOperationException("Full-trace transport records must be contiguous.");
        if (records.Length is <= 0 or > FullTraceSegmentCodec.MaximumRecords || _partialSegmentWritten)
            throw new InvalidOperationException("Only the final full-trace segment may be partial.");
        if (records[0].Id.Sequence != _nextSemanticSequence)
            throw new InvalidOperationException("Full-trace semantic records must be contiguous across segments.");

        var length = FullTraceSegmentCodec.Encode(
            _session,
            _semanticSession,
            _segmentCount,
            _nextTransportSequence,
            _serviceCapacity,
            records,
            buffer);
        _storage.CommitSegment(blockOrdinal, buffer.AsSpan(0, length));

        if (_writtenRecords == 0) _firstTimestampTicks = records[0].Payload.TimestampTicks;
        _lastTimestampTicks = records[^1].Payload.TimestampTicks;
        _partialSegmentWritten = records.Length != FullTraceSegmentCodec.MaximumRecords;
        _segmentCount++;
        _writtenRecords = checked(_writtenRecords + (ulong)records.Length);
        _segmentBytes = checked(_segmentBytes + (ulong)length);
        _nextTransportSequence = checked(_nextTransportSequence + (ulong)records.Length);
        _nextSemanticSequence = checked(_nextSemanticSequence + (ulong)records.Length);
        return length;
    }

    public void Complete(in BufferedSegmentCompletion completion)
    {
        if (checked((ulong)completion.WrittenRecords) != _writtenRecords)
            throw new InvalidOperationException("Transport and full-trace durable record counts disagree.");
        var completeness = completion.Complete
            ? FullTraceCompleteness.Complete
            : FullTraceCompleteness.Incomplete;
        var reason = completion.Complete
            ? _terminalRequest.GetRequired()
            : MapFault(completion.FaultReason);
        var firstIncompleteTransport = checked((ulong)completion.FirstIncompleteSequence);
        var firstIncompleteSemantic = completion.Complete
            ? 0
            : checked(_firstSemanticSequence + firstIncompleteTransport - 1);
        var manifest = new FullTraceManifestDocument(
            completeness,
            reason,
            _session,
            _semanticSession,
            _serviceCapacity,
            _segmentCount,
            _firstSemanticSequence,
            checked((ulong)completion.AcceptedRecords),
            _writtenRecords,
            firstIncompleteTransport,
            firstIncompleteSemantic,
            _firstTimestampTicks,
            _lastTimestampTicks,
            _segmentBytes);
        var bytes = new byte[FullTraceManifestCodec.ManifestBytes];
        FullTraceManifestCodec.Encode(in manifest, bytes);
        _storage.CommitManifest(bytes);
        Volatile.Write(ref _terminalReason, (int)reason);
        Volatile.Write(ref _manifestCommitted, 1);
    }

    private static FullTraceTerminalReason MapFault(BufferedSegmentFaultReason reason) => reason switch
    {
        BufferedSegmentFaultReason.BufferExhausted => FullTraceTerminalReason.BufferExhausted,
        BufferedSegmentFaultReason.SequenceExhausted => FullTraceTerminalReason.SequenceExhausted,
        BufferedSegmentFaultReason.WriteFailed => FullTraceTerminalReason.WriteFailed,
        BufferedSegmentFaultReason.ProducerFailed => FullTraceTerminalReason.SemanticFault,
        BufferedSegmentFaultReason.ProducerStopped => FullTraceTerminalReason.RuntimeShutdown,
        _ => throw new InvalidOperationException("This transport fault cannot publish a full-trace manifest."),
    };
}
