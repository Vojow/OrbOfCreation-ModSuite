using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

internal sealed partial class ServiceCycleReplayDefinitionAdapter<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> :
    IServiceCycleDefinition<TFrame, TConfig, TState, TAction>,
    IServiceCycleAdditionalWorkerForbiddenTypeSource
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly ServiceCycleRegistry _registry;
    private readonly IServiceCycleReplayDefinition<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> _definition;
    private readonly ServiceCycleReplaySession _session;
    private readonly LifecycleGeneration _initialLifecycle;
    private readonly ServiceCycleReplayInputBridge<TCycleInputRecord>?[] _activeBridges =
        new ServiceCycleReplayInputBridge<TCycleInputRecord>[2];
    private readonly ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>?[] _activeWorkers = new ServiceCycleReplayWorker<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord>[2];
    private ServiceCycleReplayInputBridge<TCycleInputRecord>? _pendingBridge;
    private ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>? _pendingWorker;
    private ServiceCycleReplayInputBridge<TCycleInputRecord>? _candidateBridge;
    private ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>? _candidateWorker;
    private int _traceServiceKey;
#if SERVICE_CYCLE_PROFILE
    private readonly ServiceCycleProfileProbe _profileProbe;
#endif

    internal ServiceCycleReplayDefinitionAdapter(
        ServiceCycleRegistry registry,
        IServiceCycleReplayDefinition<
            TFrame,
            TConfig,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> definition,
        ServiceCycleReplaySession session,
        LifecycleGeneration initialLifecycle
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _initialLifecycle = initialLifecycle;
#if SERVICE_CYCLE_PROFILE
        _profileProbe = profileProbe ?? throw new ArgumentNullException(nameof(profileProbe));
#endif
        if (definition is IServiceCycleFatalExceptionPolicy)
            ServiceCycleFatalExceptionPolicy.Register(this);
    }

    public ServiceId ServiceId => _definition.ServiceId;
    public WakePolicy DefaultWakePolicy => _definition.DefaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _definition.FaultRecoveryPolicy;
    Type IServiceCycleAdditionalWorkerForbiddenTypeSource.AdditionalWorkerForbiddenType => _definition.GetType();

    public IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction> CreateWorkerDefinition()
    {
        RollBackPendingPair();
        PruneReleasedWorkers();
        TrackLiveUncapturedCandidate();
        var lifecycle = _registry.CurrentLifecycle.Value != 0
            ? _registry.CurrentLifecycle
            : _initialLifecycle;
        if (lifecycle.Value == 0)
            throw new InvalidOperationException("Replay registration requires a known construction lifecycle.");
        var bridge = new ServiceCycleReplayInputBridge<TCycleInputRecord>(_session, lifecycle.Value);
        if (_traceServiceKey > 0) bridge.BindTraceServiceKey(_traceServiceKey);
        var worker = _definition.CreateWorkerDefinition() ??
            throw new InvalidOperationException("The replayable service did not create a worker definition.");

        // Let the ordinary identity ledger diagnose actual worker aliasing. Codec aliases on otherwise
        // distinct physical workers are rejected here before frame construction.
        if (!IsActualWorkerAlias(worker)) EnsureIndependentCodecs(worker);
        if (_traceServiceKey > 0) BindCodecManifest(worker);
        _pendingBridge = bridge;
        _pendingWorker = worker;
        return worker;
    }

    public TFrame CreateFrame()
    {
        var bridge = _pendingBridge ??
            throw new InvalidOperationException("Replay frame construction was not paired with a worker definition.");
        var worker = _pendingWorker ??
            throw new InvalidOperationException("Replay frame construction lost its paired worker definition.");
        _pendingBridge = null;
        _pendingWorker = null;
        var frame = default(TFrame)!;
        var created = false;
        try
        {
            frame = _definition.CreateFrame();
            created = true;
            worker.Attach(_session, bridge, _definition.DefaultWakePolicy);
            bridge.MarkFrameReady();
            _candidateBridge = bridge;
            _candidateWorker = worker;
            return frame;
        }
        catch (Exception primary)
        {
            bridge.MarkReleased();
            Exception? cleanupFailure = null;
            if (created)
            {
                try
                {
                    ((IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>)worker)
                        .ReleaseFrame(ref frame);
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }
            if (!ServiceCycleFatalExceptionPolicy.MustEscape(this, primary) &&
                cleanupFailure is not null &&
                ServiceCycleFatalExceptionPolicy.MustEscape(this, cleanupFailure))
                throw cleanupFailure;
            throw;
        }
    }

}
