using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed partial class ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    private readonly IServiceCycleDefinition<TFrame, TConfig, TState, TAction> _definition;
    private readonly ServiceConfigurationPublisher<TConfig> _configuration;
    private readonly ServiceFrameStorage<TFrame> _frame;
    private readonly ServiceCycleHandoff<TConfig> _handoff;
    private readonly ServiceCycleMainState<TConfig> _state;
    private readonly ServiceFaultTracker _captureFaults;
    private readonly IMonotonicClock _clock;
    private readonly ServiceId _serviceId;
    private readonly LifecycleGeneration _lifecycle;
    private readonly ServiceRunnerLifetime _lifetime;
    private ulong _captureSequence;
    private ulong _cycleSequence;
    private ulong _batchSequence;
    private bool _hasPendingRequest;
    private ConfigurationPublication<TConfig>? _pendingConfiguration;
    private ServiceCycleContext _pendingContext;
    private BatchId _pendingBatch;
    private ServiceStartDecisionFact _pendingStart;

    internal ServiceCycleStartCoordinator(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        ServiceFrameStorage<TFrame> frame,
        ServiceCycleHandoff<TConfig> handoff,
        ServiceCycleMainState<TConfig> state,
        ServiceId serviceId,
        LifecycleGeneration lifecycle,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        ServiceRunnerLifetime lifetime)
    {
        _definition = definition;
        _configuration = configuration;
        _frame = frame;
        _handoff = handoff;
        _state = state;
        _serviceId = serviceId;
        _lifecycle = lifecycle;
        _captureFaults = new ServiceFaultTracker(faultRecoveryPolicy);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    internal bool IsCapturing { get; private set; }
    internal bool IsInvokingStartCallback { get; private set; }
    internal bool HasPendingRequest => _hasPendingRequest;

    internal bool CancelPendingRequestForEmergency()
    {
        if (!_hasPendingRequest) return false;
        _hasPendingRequest = false;
        _pendingConfiguration = null;
        _pendingContext = default;
        _pendingBatch = default;
        _pendingStart = default;
        _state.CycleConfiguration = null;
        return true;
    }

    internal void InvalidateLifecycle()
    {
        if (!_hasPendingRequest) return;
        _hasPendingRequest = false;
        _pendingConfiguration = null;
        _pendingContext = default;
        _pendingBatch = default;
        _pendingStart = default;
        _state.CycleConfiguration = null;
    }

    internal void ResetCaptureFaults() => _captureFaults.Reset();

    private ServiceCycleStartAttempt TryPublishPendingRequest(bool nonBlocking)
    {
        if (_lifetime.IsSuperseded)
        {
            InvalidateLifecycle();
            return default;
        }
        var configuration = _pendingConfiguration ??
            throw new InvalidOperationException("A deferred request lost its pinned configuration.");
        // As above, the causal queue timestamp must precede the handoff that can wake evaluation.
        var queuedAt = _clock.Now;
        var published = nonBlocking
            ? _handoff.TryPublishRequestNonBlocking(
                configuration, in _pendingContext, _pendingBatch, out _)
            : _handoff.TryPublishRequest(
                configuration, in _pendingContext, _pendingBatch, out _);
        if (!published) return default;

        var identity = _pendingContext.Identity;
        var batch = _pendingBatch;
        var startFact = _pendingStart;
        _hasPendingRequest = false;
        _pendingConfiguration = null;
        _pendingContext = default;
        _pendingBatch = default;
        _pendingStart = default;
        _state.HasWakeDue = false;
        _state.InFlightCycle = identity;
        _state.InFlightBatch = batch;
        _state.HasInFlightCycle = true;
        _captureFaults.Reset();
        return new ServiceCycleStartAttempt(
            true, startFact, default, identity, batch, queuedAt);
    }

    private ServiceFaultRecord RecordCaptureFault(MonotonicTimestamp observedAt)
    {
        var record = _captureFaults.Record(ServiceFaultCategory.Capture, observedAt);
        _state.LatestFault = record.Fault;
        _state.NextWakeDue = record.RetryDue;
        _state.HasWakeDue = true;
        return record;
    }

    private void ClearRecoveredCaptureFault(in ServiceFaultRecoveryFact recovery)
    {
        if (recovery.IsPresent && _state.LatestFault.Category == ServiceFaultCategory.Capture)
            _state.LatestFault = default;
    }
}
