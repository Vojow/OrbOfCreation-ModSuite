using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

namespace OrbAutomata;

internal interface IAutomataServiceDefinition<TFrame, TState, TAction> :
    IServiceCycleDefinition<
        TFrame,
        AutomataConfiguration,
        TState,
        TAction>
{
}

internal interface IAutomataReplayServiceDefinition<
    TFrame,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> :
    IServiceCycleReplayDefinition<
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
}
