using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Export;

/// <summary>
/// Opt-in owner-thread facade for bounded one-snapshot/one-file semantic trace export. Snapshot
/// requests only claim and fill one of two preallocated handoff slots. Canonical encoding and every
/// storage call run on the dedicated background worker.
/// </summary>
public sealed partial class ServiceCycleTraceExporter : IDisposable
{
    public const int MaximumSupportedEventCapacity = 8192;

    private readonly ServiceCycleSemanticTraceSource _source;
    private readonly IRestartAwareTraceSegmentStorage _storage;
    private readonly ServiceCycleTraceExportSlot? _first;
    private readonly ServiceCycleTraceExportSlot? _second;
    private readonly byte[]? _encodingBuffer;
    private readonly AutoResetEvent? _wake;
    private readonly Thread? _worker;
    private readonly int _maximumCommittedSnapshots;
    private readonly int _ownerThreadId;
    private int _status;
    private int _admissionClosed;
    private int _ownerOperationActive;
    private int _nextOrdinal;
    private long _acceptedSnapshots;
    private long _backpressureRejections;
    private long _unavailableRejections;
    private long _exportedSnapshots;
    private long _discardedSnapshots;
    private long _bytesWritten;
    private int _pendingSnapshots;
    private int _faultCount;
    private int _retainedSnapshots;
    private int _startupPrunedSnapshots;
    private int _staleTemporaryFilesRemoved;

    public ServiceCycleTraceExporter(
        ServiceCycleSemanticTraceSource source,
        IRestartAwareTraceSegmentStorage storage,
        ServiceCycleTraceExportOptions options = default)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _ownerThreadId = Environment.CurrentManagedThreadId;

        if (!options.Enabled)
        {
            _status = (int)ServiceCycleTraceExportStatus.Disabled;
            _admissionClosed = 1;
            return;
        }
        if (source.Capacity > MaximumSupportedEventCapacity)
            throw new ArgumentOutOfRangeException(
                nameof(source),
                $"Semantic snapshot export supports at most {MaximumSupportedEventCapacity} resident events.");

        // Capacity is deliberately identical to the source ring. A drain rooted at the default cursor
        // therefore copies the complete resident suffix and reports its exact overwritten root range.
        // Disabled composition creates neither these buffers nor a worker or wait handle.
        var eventCapacity = source.Capacity;
        _first = new ServiceCycleTraceExportSlot(eventCapacity);
        _second = new ServiceCycleTraceExportSlot(eventCapacity);
        _encodingBuffer = new byte[ServiceCycleTraceCodec.GetEncodedLength(eventCapacity)];
        _status = (int)ServiceCycleTraceExportStatus.Initializing;
        _maximumCommittedSnapshots = options.MaximumCommittedSnapshots;
        _wake = new AutoResetEvent(false);
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "OrbServiceCycleTraceExporter",
            Priority = ThreadPriority.Lowest,
        };
        _worker.Start();
    }

    /// <summary>Returns current counters without allocating or waiting for the worker.</summary>
    public ServiceCycleTraceExportMetrics Metrics()
    {
        var accepted = Interlocked.Read(ref _acceptedSnapshots);
        return new ServiceCycleTraceExportMetrics(
            ReadStatus(),
            accepted,
            Interlocked.Read(ref _backpressureRejections),
            Interlocked.Read(ref _unavailableRejections),
            Interlocked.Read(ref _exportedSnapshots),
            Interlocked.Read(ref _discardedSnapshots),
            Interlocked.Read(ref _bytesWritten),
            Volatile.Read(ref _pendingSnapshots),
            Volatile.Read(ref _retainedSnapshots),
            Volatile.Read(ref _startupPrunedSnapshots),
            Volatile.Read(ref _staleTemporaryFilesRemoved),
            Volatile.Read(ref _faultCount));
    }

    private ServiceCycleTraceExportStatus ReadStatus() =>
        (ServiceCycleTraceExportStatus)Volatile.Read(ref _status);
}
