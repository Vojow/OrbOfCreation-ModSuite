using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

/// <summary>
/// Projects a whole-suite artifact trace onto the replayable services that Execution can actually
/// compose. Ordinary-service facts are not fabricated; replay service keys are remapped to the dense
/// ordinals assigned by the isolated registry.
/// </summary>
internal static class ServiceCycleReplaySemanticProjection
{
    internal static ServiceCycleTraceDocument Create(
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleReplayTraceMap map)
    {
        var source = artifact.SemanticTrace;
        var retained = new bool[source.Count];
        var projectedPumps = new ServiceCycleSemanticPayload[source.Count];
        var preserveExactPumps = HasFullDenseTopology(source, map);
        var segmentStart = 0;
        var runtimeStartingOrdinal = 0;
        var count = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            var keep = item.Payload.Service == 0
                ? item.Kind != ServiceCycleSemanticEventKind.PumpCompleted
                : map.TryRuntimeKey(checked((int)item.Payload.Service), out _);
            if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
            {
                var payload = preserveExactPumps
                    ? item.Payload
                    : ProjectPump(source, segmentStart, index, runtimeStartingOrdinal, map);
                projectedPumps[index] = payload;
                // Exact replay retains every source pump. Accepted quiet pumps carry the fairness
                // rotation that determines which service is visited first by the next active frame.
                keep = true;
                if (payload.PumpAccepted)
                    runtimeStartingOrdinal = (runtimeStartingOrdinal + 1) % map.Count;
                segmentStart = index + 1;
            }
            retained[index] = keep;
            if (keep) count++;
        }

        var events = new ServiceCycleSemanticEvent[count];
        var identities = new Dictionary<ulong, ServiceCycleTraceEventId>(count);
        var output = 0;
        for (var index = 0; index < source.Count; index++)
        {
            if (!retained[index]) continue;
            var item = source[index];
            var id = new ServiceCycleTraceEventId(source.Session, checked((ulong)output + 1));
            var parent = ResolveParent(source, retained, identities, item.Parent);
            var originalPayload = item.Payload;
            var payload = item.Kind == ServiceCycleSemanticEventKind.PumpCompleted
                ? projectedPumps[index]
                : RemapService(in originalPayload, map);
            events[output++] = new ServiceCycleSemanticEvent(id, parent, item.Kind, in payload);
            identities.Add(item.Id.Sequence, id);
        }
        return new ServiceCycleTraceDocument(source.SchemaVersion, source.Session, default, map.Count, events);
    }

    private static bool HasFullDenseTopology(
        ServiceCycleTraceDocument source,
        ServiceCycleReplayTraceMap map)
    {
        if (source.ServiceCapacity != map.Count) return false;
        for (var key = 1; key <= map.Count; key++)
            if (!map.TryRuntimeKey(key, out var runtimeKey) || runtimeKey != key) return false;
        return true;
    }

    private static ServiceCycleTraceEventId ResolveParent(
        ServiceCycleTraceDocument source,
        bool[] retained,
        Dictionary<ulong, ServiceCycleTraceEventId> identities,
        ServiceCycleTraceEventId parent)
    {
        while (parent.IsValid)
        {
            var index = checked((int)parent.Sequence - 1);
            if ((uint)index >= (uint)source.Count)
                throw new InvalidOperationException("Replay semantic evidence has an out-of-range causal parent.");
            if (retained[index])
                return identities.TryGetValue(parent.Sequence, out var projected)
                    ? projected
                    : throw new InvalidOperationException("Replay semantic projection reordered a causal parent.");
            parent = source[index].Parent;
        }
        return default;
    }

    private static ServiceCycleSemanticPayload ProjectPump(
        ServiceCycleTraceDocument source,
        int start,
        int end,
        int startingOrdinal,
        ServiceCycleReplayTraceMap map)
    {
        var original = source[end].Payload;
        var responses = 0;
        var actions = 0;
        var captures = 0;
        var emergency = 0;
        long lifecycle = 0;
        long actionDuration = 0;
        long captureDuration = 0;
        for (var index = start; index < end; index++)
        {
            var item = source[index];
            if (item.Payload.Service == 0 ||
                !map.TryRuntimeKey(checked((int)item.Payload.Service), out _)) continue;
            switch (item.Kind)
            {
                case ServiceCycleSemanticEventKind.CycleStarted:
                    responses++;
                    break;
                case ServiceCycleSemanticEventKind.ActionAttempted:
                    actions++;
                    break;
                case ServiceCycleSemanticEventKind.CaptureStarted:
                    captures++;
                    break;
                case ServiceCycleSemanticEventKind.ActionRejected
                    when item.Payload.Code == CommonActionResultCodes.EmergencyStop.Value:
                    emergency++;
                    break;
                case ServiceCycleSemanticEventKind.LifecycleActivated:
                case ServiceCycleSemanticEventKind.LifecycleRetired:
                    lifecycle++;
                    break;
            }
            if (item.Kind is ServiceCycleSemanticEventKind.ActionCommitted or
                ServiceCycleSemanticEventKind.ActionRejected or
                ServiceCycleSemanticEventKind.ActionFaulted)
                actionDuration = checked(actionDuration + item.Payload.DurationTicks);
            if (item.Kind is ServiceCycleSemanticEventKind.CaptureCompleted or
                ServiceCycleSemanticEventKind.CaptureUnavailable or
                ServiceCycleSemanticEventKind.CaptureFaulted)
                captureDuration = checked(captureDuration + item.Payload.DurationTicks);
        }
        return ServiceCycleSemanticPayload.Pump(
            original.FrameIdentity,
            original.PumpAccepted,
            startingOrdinal,
            responses,
            actions,
            captures,
            emergency,
            lifecycle,
            responses == 0 ? 0 : original.ResponseDurationTicks,
            actionDuration,
            captureDuration,
            original.TotalDurationTicks,
            original.TimestampTicks);
    }

    private static ServiceCycleSemanticPayload RemapService(
        in ServiceCycleSemanticPayload value,
        ServiceCycleReplayTraceMap map)
    {
        if (value.Service == 0) return value;
        if (!map.TryRuntimeKey(checked((int)value.Service), out var runtimeKey))
            throw new InvalidOperationException("An ordinary service escaped replay semantic projection.");
        return new ServiceCycleSemanticPayload(
            value.Fields,
            checked((ulong)runtimeKey),
            value.Lifecycle,
            value.Configuration,
            value.Strategy,
            value.Capture,
            value.Cycle,
            value.Batch,
            value.Action,
            value.StatePublication,
            value.TimestampTicks,
            value.DurationTicks,
            value.DeadlineTicks,
            value.FrameIdentity,
            value.Fingerprint,
            value.Code,
            value.Disposition,
            value.ActionIndex,
            value.ActionCount,
            value.CommittedCount,
            value.UntouchedSuffixCount,
            value.OccurrenceCount,
            value.NativeCallsAttempted,
            value.MutationAttempts,
            value.MutationsCommitted,
            value.ResponsesAcquired,
            value.ActionsAttempted,
            value.CapturesAttempted,
            value.EmergencyBatchesRejected,
            value.LifecycleTransitions,
            value.ResponseDurationTicks,
            value.ActionDurationTicks,
            value.CaptureDurationTicks,
            value.TotalDurationTicks,
            value.NativeOutcomeCode);
    }
}
