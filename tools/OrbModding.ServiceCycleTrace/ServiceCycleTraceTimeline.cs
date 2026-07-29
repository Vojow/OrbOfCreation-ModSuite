using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace;

internal static class ServiceCycleTraceTimeline
{
    internal static List<ServiceCycleSemanticEvent> MeaningfulEvents(ServiceCycleTraceDocument trace)
    {
        var result = new List<ServiceCycleSemanticEvent>(trace.Count);
        for (var index = 0; index < trace.Count; index++)
        {
            var item = trace[index];
            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted || HasPumpActivity(item.Payload))
                result.Add(item);
        }
        result.Sort(Compare);
        return result;
    }

    internal static string Describe(in ServiceCycleSemanticEvent item)
    {
        var payload = item.Payload;
        var parts = new List<string>(8);
        Add(parts, "service", payload.Service, ServiceCycleSemanticFields.Service, payload.Fields);
        Add(parts, "lifecycle", payload.Lifecycle, ServiceCycleSemanticFields.Lifecycle, payload.Fields);
        Add(parts, "cycle", payload.Cycle, ServiceCycleSemanticFields.Cycle, payload.Fields);
        Add(parts, "batch", payload.Batch, ServiceCycleSemanticFields.Batch, payload.Fields);
        Add(parts, "action", payload.Action, ServiceCycleSemanticFields.Action, payload.Fields);
        if ((payload.Fields & ServiceCycleSemanticFields.FrameIdentity) != 0)
            parts.Add($"frame={payload.FrameIdentity}");
        if ((payload.Fields & ServiceCycleSemanticFields.Code) != 0)
            parts.Add($"code={payload.Code}");
        if ((payload.Fields & ServiceCycleSemanticFields.Duration) != 0)
            parts.Add($"duration={FormatMilliseconds(payload.DurationTicks)} ms");
        if ((payload.Fields & ServiceCycleSemanticFields.PumpCounts) != 0)
            parts.Add(
                $"responses={payload.ResponsesAcquired}, captures={payload.CapturesAttempted}, actions={payload.ActionsAttempted}");
        if ((payload.Fields & ServiceCycleSemanticFields.PumpDurations) != 0)
            parts.Add($"pump={FormatMilliseconds(payload.TotalDurationTicks)} ms");
        if ((payload.Fields & ServiceCycleSemanticFields.NativeCallTotals) != 0)
            parts.Add(
                $"native={payload.NativeCallsAttempted}/{payload.MutationAttempts}/{payload.MutationsCommitted}");
        if (payload.NativeOutcome is { } nativeOutcome)
            parts.Add($"outcome={nativeOutcome}");
        return string.Join("; ", parts);
    }

    // A held frame counts: the gate declining to start a cycle is the one thing a stalled suite does,
    // and dropping it here is what made a stall read as an idle window.
    internal static bool HasPumpActivity(ServiceCycleSemanticPayload payload) =>
        payload.ResponsesAcquired != 0 ||
        payload.CapturesAttempted != 0 ||
        payload.ActionsAttempted != 0 ||
        payload.CyclesStarted != 0 ||
        payload.WorldGateDeferrals != 0 ||
        payload.EmergencyBatchesRejected != 0 ||
        payload.LifecycleTransitions != 0;

    private static int Compare(ServiceCycleSemanticEvent left, ServiceCycleSemanticEvent right)
    {
        var timestamp = left.Payload.TimestampTicks.CompareTo(right.Payload.TimestampTicks);
        return timestamp != 0 ? timestamp : left.Id.Sequence.CompareTo(right.Id.Sequence);
    }

    private static string FormatMilliseconds(long ticks) =>
        TraceMetric.ToMilliseconds(ticks).ToString("F3", CultureInfo.InvariantCulture);

    private static void Add(
        ICollection<string> parts,
        string name,
        ulong value,
        ServiceCycleSemanticFields field,
        ServiceCycleSemanticFields available)
    {
        if ((available & field) != 0) parts.Add($"{name}={value}");
    }
}
