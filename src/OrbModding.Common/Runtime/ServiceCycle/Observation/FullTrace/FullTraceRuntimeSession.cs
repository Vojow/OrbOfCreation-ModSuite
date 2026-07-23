using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace;

internal sealed class FullTraceRuntimeSession : IDisposable
{
    private const int BlockCount = 10;

    private readonly SuiteFramePump _pump;
    private readonly int _serviceCapacity;
    private readonly int _ownerThreadId;
    private BufferedSegmentSink<ServiceCycleSemanticEvent>? _sink;
    private FullTraceSegmentConsumer? _consumer;
    private FullTraceTerminalRequest? _terminalRequest;
    private ServiceCycleSemanticRecorder? _recorder;
    private ServiceCycleSemanticRuntimeTrace? _attachedTrace;
    private FullTraceRuntimeSessionSnapshot _terminalSnapshot;
    private FullTraceTerminalReason? _requestedReason;
    private FullTraceRuntimeSessionState _state;
    private bool _sinkStopIssued;
    private bool _disposed;

    internal FullTraceRuntimeSession(SuiteFramePump pump, int serviceCapacity)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _serviceCapacity = serviceCapacity;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal FullTraceRuntimeSessionSnapshot Snapshot
    {
        get
        {
            EnsureOwner();
            if (_state is FullTraceRuntimeSessionState.Idle or
                FullTraceRuntimeSessionState.Complete or
                FullTraceRuntimeSessionState.Incomplete)
                return _terminalSnapshot;
            return CurrentSnapshot();
        }
    }

    internal void Start(
        FullTraceSessionId session,
        ServiceCycleTraceSessionId semanticSession,
        ISegmentSessionStorage storage)
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (_state is not (FullTraceRuntimeSessionState.Idle or
            FullTraceRuntimeSessionState.Complete or
            FullTraceRuntimeSessionState.Incomplete))
            throw new InvalidOperationException("A manual full-trace session is already active.");
        if (storage is null) throw new ArgumentNullException(nameof(storage));

        ReleaseTerminalResources();
        var terminalRequest = new FullTraceTerminalRequest();
        var consumer = new FullTraceSegmentConsumer(
            storage,
            terminalRequest,
            session,
            semanticSession,
            _serviceCapacity);
        BufferedSegmentSink<ServiceCycleSemanticEvent>? sink = null;
        ServiceCycleSemanticRecorder recorder;
        try
        {
            sink = new BufferedSegmentSink<ServiceCycleSemanticEvent>(
                consumer,
                new BufferedSegmentOptions(
                    BlockCount,
                    FullTraceSegmentCodec.MaximumRecords,
                    "ServiceCycle full trace writer"));
            var observer = new FullTraceSemanticEventObserver(sink);
            recorder = new ServiceCycleSemanticRecorder(
                semanticSession,
                eventCapacity: 1,
                serviceCapacity: _serviceCapacity,
                enabled: true,
                observer: observer);
        }
        catch
        {
            sink?.Dispose();
            throw;
        }
        _terminalRequest = terminalRequest;
        _consumer = consumer;
        _sink = sink;
        _recorder = recorder;
        _terminalSnapshot = default;
        _requestedReason = null;
        _sinkStopIssued = false;
        _state = FullTraceRuntimeSessionState.Arming;
    }

    internal void RequestStop()
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (_state is not (FullTraceRuntimeSessionState.Arming or FullTraceRuntimeSessionState.Recording))
            throw new InvalidOperationException("No active manual full-trace session can stop.");
        BeginStopping(FullTraceTerminalReason.UserStopped);
    }

    internal void Tick()
    {
        EnsureOwner();
        ThrowIfDisposed();
        if (_state == FullTraceRuntimeSessionState.Arming) TickArming();
        if (_state == FullTraceRuntimeSessionState.Recording) TickRecording();
        if (_state == FullTraceRuntimeSessionState.Stopping) TickStopping();
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        Shutdown();
        _sink?.Dispose();
        _disposed = true;
    }

    private void TickArming()
    {
        var sink = RequiredSink();
        var status = sink.Metrics().Status;
        if (status == BufferedSegmentStatus.Faulted)
        {
            Finish(FullTraceRuntimeSessionState.Incomplete);
            return;
        }
        if (status != BufferedSegmentStatus.Running) return;
        var recorder = _recorder ?? throw new InvalidOperationException("The arming recorder is unavailable.");
        if (!_pump.TryAttachManualSemanticTrace(recorder, out var attached)) return;
        _attachedTrace = attached ?? throw new InvalidOperationException("The pump attached no semantic trace.");
        _state = FullTraceRuntimeSessionState.Recording;
    }

    private void TickRecording()
    {
        var sink = RequiredSink();
        if (_attachedTrace?.IsFaulted == true && sink.Metrics().Status == BufferedSegmentStatus.Running)
            sink.FailProducer();
        var status = sink.Metrics().Status;
        if (_attachedTrace?.IsFaulted == true ||
            status is BufferedSegmentStatus.Faulting or BufferedSegmentStatus.Faulted)
            _state = FullTraceRuntimeSessionState.Stopping;
    }

    private void TickStopping()
    {
        var sink = RequiredSink();
        if (_attachedTrace is not null)
        {
            if (_attachedTrace.IsFaulted && sink.Metrics().Status == BufferedSegmentStatus.Running)
                sink.FailProducer();
            if (!_pump.TryDetachManualSemanticTrace(_attachedTrace)) return;
            _attachedTrace = null;
        }

        var status = sink.Metrics().Status;
        if (!_sinkStopIssued &&
            status is (BufferedSegmentStatus.Initializing or BufferedSegmentStatus.Running))
        {
            if (_requestedReason is null)
                throw new InvalidOperationException("A healthy full-trace stop requires a terminal reason.");
            sink.Stop();
            _sinkStopIssued = true;
            status = sink.Metrics().Status;
        }
        if (status == BufferedSegmentStatus.Stopped)
            Finish(FullTraceRuntimeSessionState.Complete);
        else if (status == BufferedSegmentStatus.Faulted)
            Finish(FullTraceRuntimeSessionState.Incomplete);
    }

    private void Shutdown()
    {
        if (_state is FullTraceRuntimeSessionState.Idle or
            FullTraceRuntimeSessionState.Complete or
            FullTraceRuntimeSessionState.Incomplete)
            return;

        var sink = RequiredSink();
        if (_state != FullTraceRuntimeSessionState.Stopping)
            BeginStopping(FullTraceTerminalReason.RuntimeShutdown);
        if (_attachedTrace is not null)
        {
            if (_pump.TryDetachManualSemanticTrace(_attachedTrace))
            {
                _attachedTrace = null;
            }
            else
            {
                sink.FailProducer(BufferedSegmentFaultReason.ProducerStopped);
                _pump.DiscardManualSemanticTrace(_attachedTrace);
                _attachedTrace = null;
            }
        }
        if (sink.Metrics().Status is (BufferedSegmentStatus.Initializing or BufferedSegmentStatus.Running))
            sink.Stop();
    }

    private void BeginStopping(FullTraceTerminalReason reason)
    {
        var terminalRequest = _terminalRequest ??
            throw new InvalidOperationException("The full-trace terminal channel is unavailable.");
        terminalRequest.Set(reason);
        _requestedReason = reason;
        _state = FullTraceRuntimeSessionState.Stopping;
    }

    private FullTraceRuntimeSessionSnapshot CurrentSnapshot()
    {
        var metrics = RequiredSink().Metrics();
        var terminal = _state is FullTraceRuntimeSessionState.Complete or
            FullTraceRuntimeSessionState.Incomplete;
        var manifestCommitted = terminal && _consumer?.ManifestCommitted == true;
        return new FullTraceRuntimeSessionSnapshot(
            _state,
            metrics.AcceptedRecords,
            metrics.WrittenRecords,
            checked(metrics.BytesWritten + (manifestCommitted ? FullTraceManifestCodec.ManifestBytes : 0)),
            metrics.WrittenBlocks,
            terminal ? metrics.FirstIncompleteSequence : 0,
            manifestCommitted,
            manifestCommitted
                ? _consumer?.TerminalReason ?? default
                : _requestedReason ?? default,
            metrics.FaultReason);
    }

    private void Finish(FullTraceRuntimeSessionState terminalState)
    {
        _state = terminalState;
        _terminalSnapshot = CurrentSnapshot();
        ReleaseTerminalResources();
    }

    private void ReleaseTerminalResources()
    {
        _sink?.Dispose();
        _sink = null;
        _consumer = null;
        _terminalRequest = null;
        _recorder = null;
        _attachedTrace = null;
        _requestedReason = null;
        _sinkStopIssued = false;
    }

    private BufferedSegmentSink<ServiceCycleSemanticEvent> RequiredSink() =>
        _sink ?? throw new InvalidOperationException("The full-trace sink is unavailable.");

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Manual full-trace control must remain on its owning main thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FullTraceRuntimeSession));
    }
}
