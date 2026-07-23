using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

/// <summary>
/// Fixed-capacity append-only replay payload storage. Enabled backing arrays are allocated on the first
/// worker commit. Codecs run in per-runner scratch before the short commit gate; main-thread capture only
/// uses the independent atomic failure latch.
/// </summary>
public sealed partial class ServiceCycleReplaySession : IServiceCycleReplayHighWaterSource
{
    private readonly object _commitGate = new();
    private readonly object _manifestGate = new();
    private byte[]? _bytes;
    private ServiceCycleReplayRecordHeader[]? _records;
    private ServiceCycleReplayCycleFooter[]? _footers;
    private readonly int _byteCapacity;
    private readonly int _recordCapacity;
    private readonly int _cycleFooterCapacity;
    private readonly bool _encodingEnabled;
    private readonly ServiceCycleTraceSessionId _traceSession;
    private readonly ServiceCycleReplayCodecManifest[] _codecManifests;
    private readonly ServiceCycleReplayCodecManifest[] _publishedCodecManifests;
    private readonly object?[] _codecManifestOwners;
    private readonly int[] _codecManifestBound;
    private int _codecManifestCount;
    private long _codecManifestPublication;
    private int _byteCount;
    private int _recordCount;
    private int _footerCount;
    private long _recordSequence;
    private long _footerSequence;
    private int _offlineFooterWaiterCount;
    private int _offlineFooterWakePulseCount;
    private long _fencePublication;
    private int _fenceVersion;
    private int _snapshotWriters;
    private int _failureState;
    private int _recordingAdmissionClosed;
    private ServiceCycleReplayCycleKey _firstIncompleteCycle;
    private ServiceCycleReplayCompleteness _completeness;
    private ServiceCycleReplayFault _fault;

    public ServiceCycleReplaySession(
        ServiceCycleTraceSessionId traceSession,
        ServiceCycleReplaySessionOptions options)
    {
        if (!traceSession.IsValid)
            throw new ArgumentException("A valid semantic trace session identity is required.", nameof(traceSession));
        _traceSession = traceSession;
        _encodingEnabled = options.EncodingEnabled;
        _byteCapacity = options.ByteCapacity;
        _recordCapacity = options.RecordCapacity;
        _cycleFooterCapacity = options.CycleFooterCapacity;
        if (!options.EncodingEnabled)
        {
            _bytes = Array.Empty<byte>();
            _records = Array.Empty<ServiceCycleReplayRecordHeader>();
            _footers = Array.Empty<ServiceCycleReplayCycleFooter>();
        }
        _codecManifests = new ServiceCycleReplayCodecManifest[options.ServiceCapacity];
        _publishedCodecManifests = new ServiceCycleReplayCodecManifest[options.ServiceCapacity];
        _codecManifestOwners = new object[options.ServiceCapacity];
        _codecManifestBound = new int[options.ServiceCapacity];
        _completeness = ServiceCycleReplayCompleteness.Complete;
    }

    public bool EncodingEnabled => _encodingEnabled;
    public int ByteCapacity => _byteCapacity;
    public int RecordCapacity => _recordCapacity;
    public int CycleFooterCapacity => _cycleFooterCapacity;
    public int ServiceCapacity => _codecManifests.Length;
    public ServiceCycleTraceSessionId TraceSession => _traceSession;
    public bool RecordingAdmissionClosed => Volatile.Read(ref _recordingAdmissionClosed) != 0;
    internal int OfflineFooterWaiterCount => Volatile.Read(ref _offlineFooterWaiterCount);
    internal int OfflineFooterWakePulseCount => Volatile.Read(ref _offlineFooterWakePulseCount);

    /// <summary>
    /// Prevents future cycles from entering this finite recording window. Cycles which already
    /// entered remain able to complete, so closing admission cannot create a partial footer.
    /// </summary>
    public void CloseRecordingAdmission() =>
        Interlocked.Exchange(ref _recordingAdmissionClosed, 1);

    internal bool TryBeginRecordingCycle() =>
        Volatile.Read(ref _recordingAdmissionClosed) == 0;

    internal bool CanInvokeCodec => _encodingEnabled && Volatile.Read(ref _failureState) == 0;

}
