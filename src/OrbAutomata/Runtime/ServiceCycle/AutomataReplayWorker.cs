using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class AutomataReplayWorker<
    TFrame,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> :
    ServiceCycleReplayWorker<
        TFrame,
        AutomataConfiguration,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord
{
    internal AutomataReplayWorker(
        IServiceCycleReplayEvaluatorPort<
            TFrame,
            AutomataConfiguration,
            TState,
            TAction,
            TStateRecord,
            TActionRecord> evaluator,
        IServiceCycleReplayCodec<TCycleInputRecord> inputCodec,
        IServiceCycleReplayCodec<TStateRecord> stateCodec,
        IServiceCycleReplayCodec<TActionRecord> actionCodec)
        : base(evaluator, inputCodec, stateCodec, actionCodec)
    {
    }
}
