using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal delegate TCycleInputRecord AutomataCycleInputRecordFactory<TFrame, TCycleInputRecord>(
    in TFrame frame,
    in AutomataConfiguration configuration,
    in ServiceCaptureContext context,
    in ServiceCaptureResult capture)
    where TCycleInputRecord : struct, IServiceCycleReplayRecord;

internal delegate ServiceCycleReplayWorker<
    TFrame,
    AutomataConfiguration,
    TState,
    TAction,
    TCycleInputRecord,
    TStateRecord,
    TActionRecord> AutomataReplayWorkerFactory<
        TFrame,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord>()
    where TCycleInputRecord : struct, IServiceCycleReplayRecord
    where TStateRecord : struct, IServiceCycleReplayRecord
    where TActionRecord : struct, IServiceCycleReplayRecord;

internal static class AutomataReplayService
{
    internal static IAutomataReplayServiceDefinition<
        TFrame,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> Decorate<
            TFrame,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord>(
        IAutomataServiceDefinition<TFrame, TState, TAction> service,
        AutomataCycleInputRecordFactory<TFrame, TCycleInputRecord> createCycleInputRecord,
        AutomataReplayWorkerFactory<
            TFrame,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord> createWorker)
        where TCycleInputRecord : struct, IServiceCycleReplayRecord
        where TStateRecord : struct, IServiceCycleReplayRecord
        where TActionRecord : struct, IServiceCycleReplayRecord =>
        new ComposedAutomataReplayServiceDefinition<
            TFrame,
            TState,
            TAction,
            TCycleInputRecord,
            TStateRecord,
            TActionRecord>(
                service,
                createCycleInputRecord,
                createWorker);
}
