using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Format;

internal static class ServiceCycleReplayJoinValues
{
    internal static ServiceCycleReplaySemanticJoin Simple(
        ServiceCycleReplaySemanticJoinCode code,
        ServiceCycleSemanticEventKind evaluation = 0) => new(code, evaluation, 0, 0, 0, 0, 0);

    internal static ServiceCycleReplaySemanticJoin Joined(
        ServiceCycleReplaySemanticJoinCode code,
        ServiceCycleSemanticEvent evaluation,
        ServiceCycleSemanticEvent cycle,
        ServiceCycleSemanticEvent state,
        ServiceCycleSemanticEvent published,
        ServiceCycleSemanticEvent terminal) => new(
            code,
            evaluation.Kind,
            cycle.Kind,
            state.Payload.StatePublication,
            state.Payload.Fingerprint,
            published.Payload.Batch,
            terminal.Id.Sequence);

    internal static ServiceCycleReplaySemanticJoin Aborted(
        ServiceCycleSemanticEvent evaluation,
        ServiceCycleSemanticEvent cycle) => new(
            ServiceCycleReplaySemanticJoinCode.Complete,
            evaluation.Kind,
            cycle.Kind,
            0,
            0,
            0,
            cycle.Id.Sequence);
}
