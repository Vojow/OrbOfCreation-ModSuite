using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>Typed owner-thread facade for one strict half-duplex service cycle.</summary>
public sealed partial class ServiceRunner<TState, TAction> :
    IDisposable
{
    private readonly int _ownerThreadId;
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ServiceId _serviceId;
    private readonly WakePolicy _defaultWakePolicy;
    private readonly ServiceFaultRecoveryPolicy _faultRecoveryPolicy;
    private readonly ReusableActionStore<TAction> _actions;
    private readonly ServiceCycleHandoff _handoff;
    private readonly ServiceCycleWorker<TState, TAction> _worker;
    private readonly ServiceCycleMainState _main;
    private readonly ServiceCycleStartCoordinator<TState, TAction> _starts;
    private readonly ServiceBatchResponseHandler<TState, TAction> _responses;
    private readonly ServiceBatchActionExecutor<TState, TAction> _actionExecutor;
    private readonly ServiceBatchCompletion<TState, TAction> _batchCompletion;
    private readonly ServiceRunnerDiagnosticsAssembler<TState, TAction> _diagnostics;
    private readonly ServiceRunnerLifetime _lifetime;
    private readonly ServiceRunnerResourceIdentity _resourceIdentity;
    private bool _disposed;
    private bool _retirementSignaled;

    internal ServiceRunner(
        ServiceConfigurationPublisher configuration,
        LifecycleGeneration lifecycle,
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        in ServiceRunnerParts<TState, TAction> parts)
    {
        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        _configuration = configuration;
        _serviceId = serviceId;
        _defaultWakePolicy = defaultWakePolicy;
        _faultRecoveryPolicy = faultRecoveryPolicy;
        Lifecycle = lifecycle;
        _actions = parts.Actions;
        _handoff = parts.Handoff;
        _worker = parts.Worker;
        _main = parts.Main;
        _starts = parts.Starts;
        _responses = parts.Responses;
        _actionExecutor = parts.ActionExecutor;
        _batchCompletion = parts.BatchCompletion;
        _diagnostics = parts.Diagnostics;
        _lifetime = parts.Lifetime;
        _resourceIdentity = parts.ResourceIdentity;
    }

    public ServiceId ServiceId => _serviceId;
    public WakePolicy DefaultWakePolicy => _defaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _faultRecoveryPolicy;
    public LifecycleGeneration Lifecycle { get; }
    public bool IsDisposed => _disposed;
    internal ServiceConfigurationPublisher Configuration => _configuration;
    internal string WorkerName => _worker.Name;
    internal ServiceRunnerResourceIdentity ResourceIdentity => _resourceIdentity;
    internal bool IsSuperseded => _lifetime.IsSuperseded;
    internal bool IsBetweenCycles =>
        !_starts.IsCapturing &&
        !_starts.IsInvokingStartCallback &&
        !_starts.HasPendingRequest &&
        HandoffPhaseHint is ServiceHandoffPhase.Empty or ServiceHandoffPhase.Stopped;

    public ServiceCyclePhase Phase
    {
        get
        {
            if (_starts.IsCapturing || _starts.IsInvokingStartCallback)
                return ServiceCyclePhase.Capturing;
            return _handoff.PhaseHint switch
            {
                ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating =>
                    ServiceCyclePhase.Evaluating,
                ServiceHandoffPhase.ResponseReady or ServiceHandoffPhase.MainOwnedBatch =>
                    ServiceCyclePhase.Executing,
                _ => ServiceCyclePhase.Waiting,
            };
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "Service runner access must remain on its owning main thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(
                nameof(ServiceRunner<TState, TAction>));
    }
}
