using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

public readonly struct ServiceCycleReplayHighWaterFence
{
    internal ServiceCycleReplayHighWaterFence(
        long publication,
        long recordSequence,
        long footerSequence,
        int recordCount,
        int footerCount,
        int byteCount)
    {
        Publication = publication;
        RecordSequence = recordSequence;
        FooterSequence = footerSequence;
        RecordCount = recordCount;
        FooterCount = footerCount;
        ByteCount = byteCount;
    }

    public long Publication { get; }
    public long RecordSequence { get; }
    public long FooterSequence { get; }
    public int RecordCount { get; }
    public int FooterCount { get; }
    public int ByteCount { get; }
    public bool IsValid => Publication >= 0 && RecordSequence >= 0 && FooterSequence >= 0 &&
        RecordCount >= 0 && FooterCount >= 0 && ByteCount >= 0;
}

public interface IServiceCycleReplayHighWaterSource
{
    bool TryReadHighWaterFence(out ServiceCycleReplayHighWaterFence fence);
}

/// <summary>
/// Immutable boundary for the append-only codec-manifest publication log. The count is a dense
/// publication prefix; individual manifests retain their possibly sparse trace service keys.
/// </summary>
public readonly struct ServiceCycleReplayCodecManifestFence
{
    internal ServiceCycleReplayCodecManifestFence(long publication, int count)
    {
        Publication = publication;
        Count = count;
    }

    public long Publication { get; }
    public int Count { get; }
    public bool IsValid => Publication >= 0 && Count >= 0;
}

/// <summary>
/// Allocation-free export boundary captured coherently on the main thread. All referenced session
/// storage is append-only and remains private; readers use the bounded session accessors.
/// </summary>
public readonly struct ServiceCycleReplayRecordingSnapshot
{
    internal ServiceCycleReplayRecordingSnapshot(
        ServiceCycleTraceSessionId traceSession,
        bool encodingEnabled,
        ServiceCycleReplayCodecManifestFence codecManifests,
        ServiceCycleReplayHighWaterFence highWater,
        ServiceCycleReplayCycleKey firstIncompleteCycle,
        ServiceCycleReplayCompleteness completeness,
        ServiceCycleReplayFault fault)
    {
        TraceSession = traceSession;
        EncodingEnabled = encodingEnabled;
        CodecManifests = codecManifests;
        HighWater = highWater;
        FirstIncompleteCycle = firstIncompleteCycle;
        Completeness = completeness;
        Fault = fault;
    }

    public ServiceCycleTraceSessionId TraceSession { get; }
    public bool EncodingEnabled { get; }
    public ServiceCycleReplayCodecManifestFence CodecManifests { get; }
    public ServiceCycleReplayHighWaterFence HighWater { get; }
    public ServiceCycleReplayCycleKey FirstIncompleteCycle { get; }
    public ServiceCycleReplayCompleteness Completeness { get; }
    public ServiceCycleReplayFault Fault { get; }
}
