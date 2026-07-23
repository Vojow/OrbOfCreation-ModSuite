using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public readonly struct ServiceCycleReplayRecordHeader
{
    internal ServiceCycleReplayRecordHeader(
        long sequence,
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordIdentity identity,
        ushort schemaVersion,
        int byteOffset,
        int byteLength)
    {
        Sequence = sequence;
        Cycle = cycle;
        Identity = identity;
        SchemaVersion = schemaVersion;
        ByteOffset = byteOffset;
        ByteLength = byteLength;
    }

    public long Sequence { get; }
    public ServiceCycleReplayCycleKey Cycle { get; }
    public ServiceCycleReplayRecordIdentity Identity { get; }
    public ushort SchemaVersion { get; }
    public int ByteOffset { get; }
    public int ByteLength { get; }
}

public enum ServiceCycleReplayCycleFooterDisposition
{
    Provisional = 1,
    EvaluationAborted = 2,
    ProjectionAborted = 3,
}

/// <summary>
/// Worker evidence only. A provisional footer does not claim ResponseReady, action publication, or any
/// native outcome; artifact assembly must join it to authoritative semantic evidence.
/// </summary>
public readonly struct ServiceCycleReplayCycleFooter
{
    internal ServiceCycleReplayCycleFooter(
        long sequence,
        ServiceCycleReplayContext context,
        ServiceCycleReplayCycleFooterDisposition disposition,
        WakePolicy returnedWake,
        bool hasReturnedWake,
        ServiceStateProjectionSnapshot projection,
        bool hasProjection,
        int expectedActionCount,
        long firstRecordSequence,
        long lastRecordSequence,
        int retainedRecordCount,
        ServiceCycleReplayCompleteness completeness,
        long encodingDurationTicks,
        long encodingTimestampFrequency,
        long encodingAllocatedBytes)
    {
        Sequence = sequence;
        Context = context;
        Disposition = disposition;
        ReturnedWake = returnedWake;
        HasReturnedWake = hasReturnedWake;
        Projection = projection;
        HasProjection = hasProjection;
        ExpectedActionCount = expectedActionCount;
        FirstRecordSequence = firstRecordSequence;
        LastRecordSequence = lastRecordSequence;
        RetainedRecordCount = retainedRecordCount;
        Completeness = completeness;
        EncodingDurationTicks = encodingDurationTicks;
        EncodingTimestampFrequency = encodingTimestampFrequency;
        EncodingAllocatedBytes = encodingAllocatedBytes;
    }

    public long Sequence { get; }
    public ServiceCycleReplayContext Context { get; }
    public ServiceCycleReplayCycleFooterDisposition Disposition { get; }
    public WakePolicy ReturnedWake { get; }
    public bool HasReturnedWake { get; }
    public ServiceStateProjectionSnapshot Projection { get; }
    public bool HasProjection { get; }
    public int ExpectedActionCount { get; }
    public long FirstRecordSequence { get; }
    public long LastRecordSequence { get; }
    /// <summary>
    /// Number of this cycle's retained records. Global worker commits may interleave, so first/last are
    /// inclusive bounds and this count never claims that every sequence inside those bounds belongs here.
    /// </summary>
    public int RetainedRecordCount { get; }
    public ServiceCycleReplayCompleteness Completeness { get; }
    public long EncodingDurationTicks { get; }
    public long EncodingTimestampFrequency { get; }
    public long EncodingAllocatedBytes { get; }

    internal ServiceCycleReplayCycleFooter WithSequence(long sequence) => new(
        sequence,
        Context,
        Disposition,
        ReturnedWake,
        HasReturnedWake,
        Projection,
        HasProjection,
        ExpectedActionCount,
        FirstRecordSequence,
        LastRecordSequence,
        RetainedRecordCount,
        Completeness,
        EncodingDurationTicks,
        EncodingTimestampFrequency,
        EncodingAllocatedBytes);
}
