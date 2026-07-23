using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Registration;

/// <summary>
/// Explicit opt-in replayable feature boundary. The adapter registers the original frame, configuration,
/// state, and action types with the ordinary registry; detached record types never enter the runner.
/// </summary>
public interface IServiceCycleReplayDefinition<
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
    ServiceCycleReplayWorker<
        TFrame,
        TConfig,
        TState,
        TAction,
        TCycleInputRecord,
        TStateRecord,
        TActionRecord> CreateWorkerDefinition();
    ServiceStartDecision ShouldStart(in TConfig config, in ServiceCycleStartContext context);
    ServiceCaptureResult Capture(
        ref TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context);
    TCycleInputRecord CreateCycleInputRecord(
        in TFrame frame,
        in TConfig config,
        in ServiceCaptureContext context,
        in ServiceCaptureResult capture);
    ServiceActionResult TryExecute(
        in TAction action,
        in TConfig config,
        in ServiceActionContext context);
}
