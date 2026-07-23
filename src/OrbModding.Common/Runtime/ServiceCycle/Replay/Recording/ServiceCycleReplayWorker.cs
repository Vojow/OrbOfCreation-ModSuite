using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

/// <summary>
/// Audited replayable worker base. It is the only Common worker type trusted to retain recorder state and
/// explicitly implements the unchanged ordinary worker interface over the feature's original four types.
/// Feature-derived fields remain subject to the ordinary worker graph audit.
/// </summary>
[ServiceCycleTrustedWorkerStorage]
public abstract partial class ServiceCycleReplayWorker<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> :
    IServiceCycleWorkerDefinition<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    [ServiceCycleAuditedWorkerDependency]
    private readonly IServiceCycleReplayCodec<TCycleInputRecord> _cycleInputCodec;
    [ServiceCycleAuditedWorkerDependency]
    private readonly IServiceCycleReplayCodec<TStateRecord> _stateCodec;
    [ServiceCycleAuditedWorkerDependency]
    private readonly IServiceCycleReplayCodec<TActionRecord> _actionCodec;
    private readonly ServiceCycleReplayCodecDescriptor _cycleInputDescriptor;
    private readonly ServiceCycleReplayCodecDescriptor _stateDescriptor;
    private readonly ServiceCycleReplayCodecDescriptor _actionDescriptor;
    [ServiceCycleAuditedWorkerDependency(required: false)]
    private readonly IServiceCycleReplayEvaluatorPort<
        TFrame,
        TConfig,
        TState,
        TAction,
        TStateRecord,
        TActionRecord>? _evaluator;
    private ServiceCycleReplayInputBridge<TCycleInputRecord>? _inputBridge;
    private ServiceCycleReplayWorkerRecorder<TCycleInputRecord, TStateRecord, TActionRecord>? _recorder;
    private WakePolicy _defaultWakePolicy;
    private int _pendingActionCount;

    protected ServiceCycleReplayWorker(
        IServiceCycleReplayCodec<TCycleInputRecord> cycleInputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec)
    {
        _cycleInputCodec = cycleInputCodec ?? throw new ArgumentNullException(nameof(cycleInputCodec));
        _stateCodec = stateCodec ?? throw new ArgumentNullException(nameof(stateCodec));
        _actionCodec = actionCodec ?? throw new ArgumentNullException(nameof(actionCodec));
        ServiceCycleReplayRecordValidator.EnsureValid<TCycleInputRecord>();
        ServiceCycleReplayRecordValidator.EnsureValid<TStateRecord>();
        ServiceCycleReplayRecordValidator.EnsureValid<TActionRecord>();
        _cycleInputDescriptor = ReadDescriptor(_cycleInputCodec);
        _stateDescriptor = ReadDescriptor(_stateCodec);
        _actionDescriptor = ReadDescriptor(_actionCodec);
    }

    protected ServiceCycleReplayWorker(
        IServiceCycleReplayEvaluatorPort<
            TFrame,
            TConfig,
            TState,
            TAction,
            TStateRecord,
            TActionRecord> evaluator,
        IServiceCycleReplayCodec<TCycleInputRecord> cycleInputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec)
        : this(cycleInputCodec, stateCodec, actionCodec) =>
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    protected virtual TState CreateStateCore(LifecycleGeneration lifecycle) =>
        Evaluator.CreateState(lifecycle);
    protected virtual void ReleaseStateCore(ref TState state) => Evaluator.ReleaseState(ref state);
    protected virtual void ReleaseFrameCore(ref TFrame frame) => Evaluator.ReleaseFrame(ref frame);
    protected virtual TStateRecord CreateStateRecordCore(in TState state) => Evaluator.CreateStateRecord(in state);
    protected virtual WakePolicy EvaluateCore(
        in TFrame frame,
        in TConfig config,
        in ServiceCycleContext context,
        ref TState state,
        ServiceCycleReplayActionWriter<TAction, TActionRecord> actions) =>
        Evaluator.Evaluate(in frame, in config, in context, ref state, actions);
    protected virtual void ProjectStateCore(
        in TState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) => Evaluator.ProjectState(in state, in context, output);

    internal object CycleInputCodecIdentity => _cycleInputCodec;
    internal object StateCodecIdentity => _stateCodec;
    internal object ActionCodecIdentity => _actionCodec;
    internal ServiceCycleReplayCodecDescriptor CycleInputCodecDescriptor => _cycleInputDescriptor;
    internal ServiceCycleReplayCodecDescriptor StateCodecDescriptor => _stateDescriptor;
    internal ServiceCycleReplayCodecDescriptor ActionCodecDescriptor => _actionDescriptor;

    internal void Attach(
        ServiceCycleReplaySession session,
        ServiceCycleReplayInputBridge<TCycleInputRecord> inputBridge,
        WakePolicy defaultWakePolicy)
    {
        if (_recorder is not null || _inputBridge is not null)
            throw new InvalidOperationException("A replayable worker can be attached to only one physical runner.");
        if (!defaultWakePolicy.IsValid || defaultWakePolicy.Kind == WakePolicyKind.Default)
            throw new ArgumentException("Replay recording requires a concrete default wake policy.", nameof(defaultWakePolicy));
        _inputBridge = inputBridge ?? throw new ArgumentNullException(nameof(inputBridge));
        _defaultWakePolicy = defaultWakePolicy;
        _recorder = new ServiceCycleReplayWorkerRecorder<TCycleInputRecord, TStateRecord, TActionRecord>(
            session,
            _cycleInputCodec,
            _stateCodec,
            _actionCodec,
            in _cycleInputDescriptor,
            in _stateDescriptor,
            in _actionDescriptor);
        if (ServiceCycleFatalExceptionPolicy.AppliesTo(this))
            ServiceCycleFatalExceptionPolicy.Register(_recorder);
    }

    private static ServiceCycleReplayCodecDescriptor ReadDescriptor<TRecord>(
        IServiceCycleReplayCodec<TRecord> codec)
        where TRecord : struct, IServiceCycleReplayRecord
    {
        var descriptor = codec.Descriptor;
        if (ServiceCycleReplayCodecContract.ValidateDescriptor(in descriptor) !=
            ServiceCycleReplayCodecContractCode.Valid)
        {
            throw new InvalidOperationException("Replay codec descriptor was rejected.");
        }
        return descriptor;
    }

    private IServiceCycleReplayEvaluatorPort<
        TFrame,
        TConfig,
        TState,
        TAction,
        TStateRecord,
        TActionRecord> Evaluator => _evaluator ??
        throw new InvalidOperationException(
            "This replay worker must override its core operations or supply a shared evaluator port.");
}
