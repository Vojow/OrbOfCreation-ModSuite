#if SERVICE_CYCLE_PROFILE
using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Format;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal sealed class ServiceCycleProfileSegmentConsumer : IBufferedSegmentConsumer<ServiceCycleProfileRecord>
{
    private readonly ISegmentSessionStorage _storage;
    private readonly ServiceCycleProfileTerminalRequest _terminalRequest;
    private readonly ServiceCycleProfileSessionId _session;
    private readonly ServiceCycleProfileCalibration _calibration;
    private byte[]? _encodingBuffer;
    private ulong _nextRecordSequence = 1;
    private ulong _segmentCount;
    private ulong _writtenRecords;
    private ulong _segmentBytes;
    private long _minimumStartedAtRawTicks;
    private long _maximumStartedAtRawTicks;
    private int _manifestCommitted;
    private int _terminalReason;

    internal ServiceCycleProfileSegmentConsumer(
        ISegmentSessionStorage storage,
        ServiceCycleProfileTerminalRequest terminalRequest,
        ServiceCycleProfileSessionId session,
        in ServiceCycleProfileCalibration calibration)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _terminalRequest = terminalRequest ?? throw new ArgumentNullException(nameof(terminalRequest));
        if (!session.IsValid) throw new ArgumentException("A valid profile session is required.", nameof(session));
        if (!calibration.IsValid) throw new ArgumentException("A valid profile calibration is required.", nameof(calibration));
        _session = session;
        _calibration = calibration;
    }

    internal bool ManifestCommitted => Volatile.Read(ref _manifestCommitted) != 0;
    internal ServiceCycleProfileTerminalReason TerminalReason =>
        (ServiceCycleProfileTerminalReason)Volatile.Read(ref _terminalReason);

    public void Initialize()
    {
        _storage.Initialize();
        _encodingBuffer = new byte[
            ServiceCycleProfileSegmentCodec.GetEncodedLength(ServiceCycleProfileSegmentCodec.MaximumRecords)];
    }

    public int Write(long blockOrdinal, long firstRecordSequence, ReadOnlySpan<ServiceCycleProfileRecord> records)
    {
        var buffer = _encodingBuffer ?? throw new InvalidOperationException("The profile consumer is not initialized.");
        if (blockOrdinal < 0 || checked((ulong)blockOrdinal) != _segmentCount)
            throw new InvalidOperationException("Profile segment ordinals must be dense.");
        if (firstRecordSequence <= 0 || checked((ulong)firstRecordSequence) != _nextRecordSequence)
            throw new InvalidOperationException("Profile records must be contiguous across segments.");
        if (records.Length is <= 0 or > ServiceCycleProfileSegmentCodec.MaximumRecords)
            throw new InvalidOperationException("A profile segment has an invalid record count.");

        var length = ServiceCycleProfileSegmentCodec.Encode(
            _session,
            _segmentCount,
            _nextRecordSequence,
            in _calibration,
            records,
            buffer);
        _storage.CommitSegment(blockOrdinal, buffer.AsSpan(0, length));

        TimestampRange(records, out var minimum, out var maximum);
        if (_writtenRecords == 0)
        {
            _minimumStartedAtRawTicks = minimum;
            _maximumStartedAtRawTicks = maximum;
        }
        else
        {
            _minimumStartedAtRawTicks = Math.Min(_minimumStartedAtRawTicks, minimum);
            _maximumStartedAtRawTicks = Math.Max(_maximumStartedAtRawTicks, maximum);
        }
        _segmentCount++;
        _writtenRecords = checked(_writtenRecords + (ulong)records.Length);
        _segmentBytes = checked(_segmentBytes + (ulong)length);
        _nextRecordSequence = checked(_nextRecordSequence + (ulong)records.Length);
        return length;
    }

    public void Complete(in BufferedSegmentCompletion completion)
    {
        if (checked((ulong)completion.WrittenRecords) != _writtenRecords)
            throw new InvalidOperationException("Transport and profile durable record counts disagree.");
        var reason = completion.Complete
            ? _terminalRequest.GetRequired()
            : MapFault(completion.FaultReason);
        var complete = completion.Complete && reason != ServiceCycleProfileTerminalReason.ProbeFailed;
        var completeness = complete
            ? ServiceCycleProfileCompleteness.Complete
            : ServiceCycleProfileCompleteness.Incomplete;
        var firstIncompleteSequence = complete
            ? 0UL
            : completion.Complete
                ? checked(_writtenRecords + 1)
                : checked((ulong)completion.FirstIncompleteSequence);
        var manifest = new ServiceCycleProfileManifestDocument(
            completeness,
            reason,
            _session,
            in _calibration,
            _segmentCount,
            checked((ulong)completion.AcceptedRecords),
            _writtenRecords,
            firstIncompleteSequence,
            _segmentBytes,
            _minimumStartedAtRawTicks,
            _maximumStartedAtRawTicks);
        var bytes = new byte[ServiceCycleProfileManifestCodec.ManifestBytes];
        ServiceCycleProfileManifestCodec.Encode(in manifest, bytes);
        _storage.CommitManifest(bytes);
        Volatile.Write(ref _terminalReason, (int)reason);
        Volatile.Write(ref _manifestCommitted, 1);
    }

    private static ServiceCycleProfileTerminalReason MapFault(BufferedSegmentFaultReason reason) => reason switch
    {
        BufferedSegmentFaultReason.BufferExhausted => ServiceCycleProfileTerminalReason.BufferExhausted,
        BufferedSegmentFaultReason.SequenceExhausted => ServiceCycleProfileTerminalReason.SequenceExhausted,
        BufferedSegmentFaultReason.WriteFailed => ServiceCycleProfileTerminalReason.WriteFailed,
        _ => throw new InvalidOperationException("This transport fault cannot publish a profile manifest."),
    };

    private static void TimestampRange(
        ReadOnlySpan<ServiceCycleProfileRecord> records,
        out long minimum,
        out long maximum)
    {
        minimum = long.MaxValue;
        maximum = long.MinValue;
        for (var index = 0; index < records.Length; index++)
        {
            minimum = Math.Min(minimum, records[index].FirstStartedAtRawTicks);
            maximum = Math.Max(maximum, records[index].LastStartedAtRawTicks);
        }
    }
}
#endif
