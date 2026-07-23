using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static partial class ServiceCycleReplayProductionCoordinator
{
    internal static bool RequiresResponseReady(ServiceCycleSemanticEventKind kind) =>
        kind == ServiceCycleSemanticEventKind.CycleStarted;

    internal static ServiceCycleReplayCycleKey FindDelayedRequestPublication(
        ServiceCycleTraceDocument semantic,
        int[] artifactKeys)
    {
        for (var index = 0; index < semantic.Count; index++)
        {
            var completed = semantic[index];
            if (completed.Kind != ServiceCycleSemanticEventKind.CaptureCompleted ||
                !IncludesService(completed.Payload.Service, artifactKeys) ||
                !TryCycle(completed.Payload, out var cycle)) continue;
            var queuedInSamePump = false;
            for (var candidateIndex = index + 1; candidateIndex < semantic.Count; candidateIndex++)
            {
                var candidate = semantic[candidateIndex];
                if (candidate.Kind == ServiceCycleSemanticEventKind.PumpCompleted) break;
                if (candidate.Kind != ServiceCycleSemanticEventKind.CycleQueued ||
                    !TryCycle(candidate.Payload, out var queued) || queued != cycle) continue;
                queuedInSamePump = true;
                break;
            }
            if (!queuedInSamePump) return cycle;
        }
        return default;
    }

    private static bool IncludesService(ulong service, int[] artifactKeys)
    {
        for (var index = 0; index < artifactKeys.Length; index++)
            if (service == (ulong)artifactKeys[index]) return true;
        return false;
    }

    internal static bool TryInitialLifecycle(
        ServiceCycleTraceDocument semantic,
        int[] artifactKeys,
        out LifecycleGeneration lifecycle)
    {
        lifecycle = default;
        if (artifactKeys.Length == 0) return false;
        ulong shared = 0;
        for (var keyIndex = 0; keyIndex < artifactKeys.Length; keyIndex++)
        {
            var key = checked((ulong)artifactKeys[keyIndex]);
            ulong activated = 0;
            for (var eventIndex = 0; eventIndex < semantic.Count; eventIndex++)
            {
                var item = semantic[eventIndex];
                if (item.Payload.Service != key) continue;
                if (TryCycle(item.Payload, out _)) break;
                if (item.Kind != ServiceCycleSemanticEventKind.LifecycleActivated) continue;
                activated = item.Payload.Lifecycle;
                break;
            }
            if (activated == 0 || shared != 0 && activated != shared) return false;
            shared = activated;
        }
        if (shared == 0) return false;
        lifecycle = new LifecycleGeneration(shared);
        return true;
    }

    internal static ServiceCycleReplayCycleKey FindIndependentPumpTimingFailure(
        ServiceCycleTraceDocument semantic,
        int[] artifactKeys,
        ServiceCycleReplayCycleKey fallback)
    {
        // Partial replay does not own suite-wide pump aggregates. Dense replay can
        // reconstruct action and capture totals from their terminal semantic facts.
        if (semantic.ServiceCapacity != artifactKeys.Length) return default;
        for (var key = 1; key <= artifactKeys.Length; key++)
            if (artifactKeys[key - 1] != key) return default;

        var segmentStart = 0;
        for (var index = 0; index < semantic.Count; index++)
        {
            var pumpEvent = semantic[index];
            if (pumpEvent.Kind != ServiceCycleSemanticEventKind.PumpCompleted) continue;
            long actionDuration = 0;
            long captureDuration = 0;
            try
            {
                for (var candidateIndex = segmentStart; candidateIndex < index; candidateIndex++)
                {
                    var candidate = semantic[candidateIndex];
                    if (candidate.Kind is ServiceCycleSemanticEventKind.ActionCommitted or
                        ServiceCycleSemanticEventKind.ActionRejected or
                        ServiceCycleSemanticEventKind.ActionFaulted)
                        actionDuration = checked(actionDuration + candidate.Payload.DurationTicks);
                    if (candidate.Kind is ServiceCycleSemanticEventKind.CaptureCompleted or
                        ServiceCycleSemanticEventKind.CaptureUnavailable or
                        ServiceCycleSemanticEventKind.CaptureFaulted)
                        captureDuration = checked(captureDuration + candidate.Payload.DurationTicks);
                }
                var pump = pumpEvent.Payload;
                var phaseDuration = checked(
                    pump.ResponseDurationTicks + pump.ActionDurationTicks + pump.CaptureDurationTicks);
                if (pump.ActionDurationTicks != actionDuration ||
                    pump.CaptureDurationTicks != captureDuration ||
                    phaseDuration > pump.TotalDurationTicks)
                    return FirstCycleInSegment(semantic, segmentStart, index, fallback);
            }
            catch (OverflowException)
            {
                return FirstCycleInSegment(semantic, segmentStart, index, fallback);
            }
            segmentStart = index + 1;
        }
        return default;
    }

    private static ServiceCycleReplayCycleKey FirstCycleInSegment(
        ServiceCycleTraceDocument semantic,
        int start,
        int end,
        ServiceCycleReplayCycleKey fallback)
    {
        for (var index = start; index < end; index++)
            if (TryCycle(semantic[index].Payload, out var cycle)) return cycle;
        return fallback;
    }
}
