using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Observability;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

/// <summary>
/// Standalone two-slot replay artifact exporter. The owner thread only copies a bounded slice of frozen
/// semantic evidence, captures one coherent replay snapshot, publishes the slot, and signals the worker.
/// Slot and encoding buffers are allocated by that worker.
/// </summary>
public sealed partial class ServiceCycleReplayArtifactExporter : IDisposable
{
    public const int MaximumSupportedSemanticEventCapacity = 8192;

    private readonly ServiceCycleSemanticTraceSource _semantic;
    private readonly ServiceCycleReplaySession _recording;
    private readonly IRestartAwareTraceSegmentStorage _storage;
    private readonly IServiceCycleReplayExportObserver? _observer;
    private ServiceCycleReplayExportSlot? _first;
    private ServiceCycleReplayExportSlot? _second;
    private readonly AutoResetEvent? _wake;
    private readonly Thread? _worker;
    private readonly ServiceCycleReplayFrozenSnapshotStager _stager;
    private readonly int _maximumCommitted;
    private readonly int _semanticCapacity;
    private readonly int _ownerThreadId;
    private int _status;
    private int _admissionClosed;
    private int _ownerOperationActive;
    private int _nextOrdinal;
    private long _accepted;
    private long _backpressured;
    private long _snapshotContended;
    private long _unavailable;
    private long _exported;
    private long _discarded;
    private long _bytesWritten;
    private long _semanticEventsCopied;
    private int _peakSemanticEventsCopiedPerRequest;
    private int _pending;
    private int _retained;
    private int _startupPruned;
    private int _staleTemporaryRemoved;
    private int _faults;

    public ServiceCycleReplayArtifactExporter(
        ServiceCycleSemanticTraceSource semantic,
        ServiceCycleReplaySession recording,
        IRestartAwareTraceSegmentStorage storage,
        ServiceCycleReplayExportOptions options = default)
        : this(semantic, recording, storage, options, observer: null) { }

    public ServiceCycleReplayArtifactExporter(
        ServiceCycleSemanticTraceSource semantic,
        ServiceCycleReplaySession recording,
        IRestartAwareTraceSegmentStorage storage,
        ServiceCycleReplayExportOptions options,
        IServiceCycleReplayExportObserver? observer)
    {
        _semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _observer = observer;
        _stager = new ServiceCycleReplayFrozenSnapshotStager(_semantic);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        if (recording.TraceSession != semantic.Session)
            throw new ArgumentException("Semantic and replay recording sessions must match.", nameof(recording));
        if (!options.Enabled)
        {
            _status = (int)ServiceCycleReplayExportStatus.Disabled;
            _admissionClosed = 1;
            return;
        }
        if (semantic.Capacity > MaximumSupportedSemanticEventCapacity)
            throw new ArgumentOutOfRangeException(nameof(semantic));
        _semanticCapacity = semantic.Capacity;
        _ = ServiceCycleReplayArtifactCodec.GetMaximumEncodedLength(_semanticCapacity, recording);
        _maximumCommitted = options.MaximumCommittedArtifacts;
        _status = (int)ServiceCycleReplayExportStatus.Initializing;
        _wake = new AutoResetEvent(false);
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "OrbServiceCycleReplayExporter",
            Priority = ThreadPriority.Lowest,
        };
        _worker.Start();
    }

    public ServiceCycleReplayExportMetrics Metrics() => new(
        ReadStatus(),
        Interlocked.Read(ref _accepted),
        Interlocked.Read(ref _backpressured),
        Interlocked.Read(ref _snapshotContended),
        Interlocked.Read(ref _unavailable),
        Interlocked.Read(ref _exported),
        Interlocked.Read(ref _discarded),
        Interlocked.Read(ref _bytesWritten),
        Interlocked.Read(ref _semanticEventsCopied),
        Volatile.Read(ref _peakSemanticEventsCopiedPerRequest),
        Volatile.Read(ref _pending),
        Volatile.Read(ref _retained),
        Volatile.Read(ref _startupPruned),
        Volatile.Read(ref _staleTemporaryRemoved),
        Volatile.Read(ref _faults));

}
