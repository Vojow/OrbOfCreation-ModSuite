using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static partial class ServiceCycleReplayProductionCoordinator
{
    private static ServiceCycleTraceDocument? SnapshotSemantic(
        ServiceCycleSemanticRecorder semantic,
        ServiceCycleReplayCycleKey location,
        out ServiceCycleReplayExecutionResult failure)
    {
        var events = new ServiceCycleSemanticEvent[semantic.Count];
        var drain = semantic.DrainSince(default, events);
        if (drain.Copied != semantic.Count || !drain.IsComplete || drain.HasMore)
        {
            failure = ServiceCycleReplayProductionResult.Fault(
                location, ServiceCycleReplayExecutionDetailCode.SemanticEventCountRejected);
            return null;
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(
            semantic.Session, default, semantic.ServiceCapacity, events, bytes);
        failure = default;
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private static bool TryParticipant(
        IServiceCycleReplayProductionParticipant[] participants,
        int traceServiceKey,
        out IServiceCycleReplayProductionParticipant participant)
    {
        var index = traceServiceKey - 1;
        if ((uint)index < (uint)participants.Length &&
            participants[index].TraceServiceKey == traceServiceKey)
        {
            participant = participants[index];
            return true;
        }
        participant = null!;
        return false;
    }

    private static ServiceCycleReplayCycleKey CycleForService(
        ServiceCycleReplayArtifactDocument artifact,
        int traceServiceKey,
        ServiceCycleReplayCycleKey fallback)
    {
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var cycle = artifact.GetCycle(index).Key;
            if (cycle.TraceServiceKey == traceServiceKey) return cycle;
        }
        return fallback;
    }

    private static bool TryCycle(
        ServiceCycleSemanticPayload payload,
        out ServiceCycleReplayCycleKey cycle)
    {
        if (payload.Service != 0 && payload.Lifecycle != 0 && payload.Configuration != 0 &&
            payload.Strategy != 0 && payload.Capture != 0 && payload.Cycle != 0)
        {
            cycle = new ServiceCycleReplayCycleKey(
                checked((int)payload.Service), payload.Lifecycle, payload.Configuration,
                payload.Strategy, payload.Capture, payload.Cycle);
            return true;
        }
        cycle = default;
        return false;
    }

    private static int TotalCycles(IServiceCycleReplayProductionParticipant[] participants)
    {
        var total = 0;
        for (var index = 0; index < participants.Length; index++)
            total = checked(total + participants[index].CycleCount);
        return total;
    }
}
