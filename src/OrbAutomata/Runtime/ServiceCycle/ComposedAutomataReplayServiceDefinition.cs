using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class ComposedAutomataReplayServiceDefinition<
    TFrame,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> :
    IAutomataReplayServiceDefinition<
        TFrame,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    private readonly IAutomataServiceDefinition<TFrame, TState, TAction> _service;
    private readonly AutomataCycleInputRecordFactory<TFrame, TCycleInputRecord> _createCycleInputRecord;
    private readonly AutomataReplayWorkerFactory<
        TFrame,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> _createWorker;

    internal ComposedAutomataReplayServiceDefinition(
        IAutomataServiceDefinition<TFrame, TState, TAction> service,
        AutomataCycleInputRecordFactory<TFrame, TCycleInputRecord> createCycleInputRecord,
        AutomataReplayWorkerFactory<
            TFrame,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> createWorker)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _createCycleInputRecord = createCycleInputRecord ??
            throw new ArgumentNullException(nameof(createCycleInputRecord));
        _createWorker = createWorker ?? throw new ArgumentNullException(nameof(createWorker));
    }

    public ServiceId ServiceId => _service.ServiceId;
    public WakePolicy DefaultWakePolicy => _service.DefaultWakePolicy;
    public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => _service.FaultRecoveryPolicy;
    public TFrame CreateFrame() => _service.CreateFrame();

    public ServiceCycleReplayWorker<
        TFrame,
        AutomataConfiguration,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> CreateWorkerDefinition() =>
        _createWorker() ??
        throw new InvalidOperationException("The replay decorator did not create a worker definition.");

    public ServiceStartDecision ShouldStart(
        in AutomataConfiguration config,
        in ServiceCycleStartContext context) =>
        _service.ShouldStart(in config, in context);

    public ServiceCaptureResult Capture(
        ref TFrame frame,
        in AutomataConfiguration config,
        in ServiceCaptureContext context) =>
        _service.Capture(ref frame, in config, in context);

    public TCycleInputRecord CreateCycleInputRecord(
        in TFrame frame,
        in AutomataConfiguration config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture) =>
        _createCycleInputRecord(in frame, in config, in context, in capture);

    public ServiceActionResult TryExecute(
        in TAction action,
        in AutomataConfiguration config,
        in ServiceActionContext context) =>
        _service.TryExecute(in action, in config, in context);
}
