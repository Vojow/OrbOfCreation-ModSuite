using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Explicit fresh-component composition for one replayable service. Implementations must return new
/// codec, comparer, hydrator and evaluator instances for every call; no live gameplay adapter is accepted.
/// </summary>
public interface IServiceCycleReplayExecutionFactory<
    TFrame,
    TConfig,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord>
    where TConfig : notnull
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    ServiceId ServiceId { get; }
    WakePolicy DefaultWakePolicy { get; }
    ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
    TFrame CreateFrame();
    IServiceCycleReplayCodec<TCycleInputRecord> CreateCycleInputCodec();
    IServiceCycleReplayCodec<TStateRecord> CreateStateCodec();
    IServiceCycleReplayCodec<TActionRecord> CreateActionCodec();
    IServiceCycleReplayComparer<TCycleInputRecord> CreateCycleInputComparer();
    IServiceCycleReplayComparer<TStateRecord> CreateStateComparer();
    IServiceCycleReplayComparer<TActionRecord> CreateActionComparer();
    IServiceCycleReplayHydrator<TFrame, TConfig, TState, TCycleInputRecord, TStateRecord> CreateHydrator();
    IServiceCycleReplayEvaluatorPort<
        TFrame, TConfig, TState, TAction, TStateRecord, TActionRecord> CreateEvaluatorPort();
    ServiceCycleReplayWorker<
        TFrame, TConfig, TState, TAction, TCycleInputRecord, TStateRecord, TActionRecord>
        CreateProductionWorkerDefinition();
}
