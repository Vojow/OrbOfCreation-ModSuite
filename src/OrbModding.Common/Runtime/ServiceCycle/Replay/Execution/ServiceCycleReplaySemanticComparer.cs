using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

public static class ServiceCycleReplaySemanticComparer
{
    public static ServiceCycleReplayCycleMismatch? Compare(
        ServiceCycleReplayCycleKey location,
        ServiceCycleTraceDocument expected,
        ServiceCycleTraceDocument actual)
    {
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        if (actual is null) throw new ArgumentNullException(nameof(actual));
        if (expected.Count != actual.Count)
            return Mismatch(Location(expected, actual, Math.Min(expected.Count, actual.Count), location), 1);
        for (var index = 0; index < expected.Count; index++)
        {
            var left = expected[index];
            var right = actual[index];
            var exactLocation = Location(expected, actual, index, location);
            if (left.Id != right.Id) return Mismatch(exactLocation, 2, Element(left, index));
            if (left.Parent != right.Parent) return Mismatch(exactLocation, 3, Element(left, index));
            if (left.Kind != right.Kind) return Mismatch(exactLocation, 4, Element(left, index));
            var leftPayload = left.Payload;
            var rightPayload = right.Payload;
            var payloadField = PayloadField(in leftPayload, in rightPayload);
            if (payloadField != 0) return Mismatch(exactLocation, payloadField, Element(left, index));
        }

        var expectedBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(expected.Count)];
        var actualBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(actual.Count)];
        var expectedLength = ServiceCycleTraceCodec.Encode(
            expected.Session,
            expected.Dropped,
            expected.ServiceCapacity,
            expected.Events,
            expectedBytes);
        var actualLength = ServiceCycleTraceCodec.Encode(
            actual.Session,
            actual.Dropped,
            actual.ServiceCapacity,
            actual.Events,
            actualBytes);
        return expectedLength == actualLength &&
            expectedBytes.AsSpan(0, expectedLength).SequenceEqual(actualBytes.AsSpan(0, actualLength))
            ? null
            : Mismatch(Location(expected, actual, 0, location), 40);
    }

    private static int PayloadField(
        in ServiceCycleSemanticPayload left,
        in ServiceCycleSemanticPayload right)
    {
        if (left.Fields != right.Fields) return 5;
        if (left.Service != right.Service) return 6;
        if (left.Lifecycle != right.Lifecycle) return 7;
        if (left.Configuration != right.Configuration) return 8;
        if (left.Strategy != right.Strategy) return 9;
        if (left.Capture != right.Capture) return 10;
        if (left.Cycle != right.Cycle) return 11;
        if (left.Batch != right.Batch) return 12;
        if (left.Action != right.Action) return 13;
        if (left.StatePublication != right.StatePublication) return 14;
        if (left.TimestampTicks != right.TimestampTicks) return 15;
        if (left.DurationTicks != right.DurationTicks) return 16;
        if (left.DeadlineTicks != right.DeadlineTicks) return 17;
        if (left.FrameIdentity != right.FrameIdentity) return 18;
        if (left.Fingerprint != right.Fingerprint) return 19;
        if (left.Code != right.Code) return 20;
        if (left.Disposition != right.Disposition) return 21;
        if (left.ActionIndex != right.ActionIndex) return 22;
        if (left.ActionCount != right.ActionCount) return 23;
        if (left.CommittedCount != right.CommittedCount) return 24;
        if (left.UntouchedSuffixCount != right.UntouchedSuffixCount) return 25;
        if (left.OccurrenceCount != right.OccurrenceCount) return 26;
        if (left.NativeCallsAttempted != right.NativeCallsAttempted) return 27;
        if (left.MutationAttempts != right.MutationAttempts) return 28;
        if (left.MutationsCommitted != right.MutationsCommitted) return 29;
        if (left.ResponsesAcquired != right.ResponsesAcquired) return 30;
        if (left.ActionsAttempted != right.ActionsAttempted) return 31;
        if (left.CapturesAttempted != right.CapturesAttempted) return 32;
        if (left.EmergencyBatchesRejected != right.EmergencyBatchesRejected) return 33;
        if (left.LifecycleTransitions != right.LifecycleTransitions) return 34;
        if (left.ResponseDurationTicks != right.ResponseDurationTicks) return 35;
        if (left.ActionDurationTicks != right.ActionDurationTicks) return 36;
        if (left.CaptureDurationTicks != right.CaptureDurationTicks) return 37;
        if (left.TotalDurationTicks != right.TotalDurationTicks) return 38;
        if (left.NativeOutcome != right.NativeOutcome) return 39;
        return 0;
    }

    private static int Element(ServiceCycleSemanticEvent item, int eventIndex) =>
        item.Kind is ServiceCycleSemanticEventKind.ActionAttempted or
            ServiceCycleSemanticEventKind.ActionCommitted or
            ServiceCycleSemanticEventKind.ActionRejected or
            ServiceCycleSemanticEventKind.ActionFaulted
            ? Math.Max(0, item.Payload.ActionIndex)
            : eventIndex;

    private static ServiceCycleReplayCycleKey Location(
        ServiceCycleTraceDocument expected,
        ServiceCycleTraceDocument actual,
        int index,
        ServiceCycleReplayCycleKey fallback)
    {
        if ((uint)index < (uint)expected.Count && TryCycle(expected[index].Payload, out var cycle))
            return cycle;
        if ((uint)index < (uint)actual.Count && TryCycle(actual[index].Payload, out cycle))
            return cycle;
        var service = (uint)index < (uint)expected.Count ? expected[index].Payload.Service : 0;
        if (service == 0 && (uint)index < (uint)actual.Count) service = actual[index].Payload.Service;
        if (service != 0)
            for (var eventIndex = 0; eventIndex < expected.Count; eventIndex++)
                if (expected[eventIndex].Payload.Service == service &&
                    TryCycle(expected[eventIndex].Payload, out cycle)) return cycle;
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

    private static ServiceCycleReplayCycleMismatch Mismatch(
        ServiceCycleReplayCycleKey location,
        int field,
        int element = 0) => new(
        location,
        new ServiceCycleReplayMismatch(
            ServiceCycleReplayMismatchCode.SemanticEvent,
            default,
            field,
            element));
}
