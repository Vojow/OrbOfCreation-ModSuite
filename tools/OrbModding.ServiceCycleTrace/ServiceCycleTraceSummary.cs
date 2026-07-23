using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace;

internal sealed class ServiceCycleTraceSummary
{
    private ServiceCycleTraceSummary() { }

    internal double WindowMilliseconds { get; private set; }
    internal TraceMetric Pump { get; private set; }
    internal TraceMetric PumpResponses { get; private set; }
    internal TraceMetric PumpCaptures { get; private set; }
    internal TraceMetric PumpActions { get; private set; }
    internal TraceMetric Capture { get; private set; }
    internal TraceMetric WorkerProcessing { get; private set; }
    internal TraceMetric Action { get; private set; }
    internal TraceMetric EndToEnd { get; private set; }
    internal TraceMetric ReplayRecordEncoding { get; private set; }
    internal int CommittedActions { get; private set; }
    internal int RejectedActions { get; private set; }
    internal int FaultedActions { get; private set; }
    internal long NativeCalls { get; private set; }
    internal long MutationAttempts { get; private set; }
    internal long MutationsCommitted { get; private set; }
    internal long ReplayRecordEncodingAllocatedBytes { get; private set; }

    internal static ServiceCycleTraceSummary Create(ServiceCycleReplayArtifactDocument artifact)
    {
        var result = new ServiceCycleTraceSummary();
        var pump = new TraceMetricBuilder();
        var pumpResponses = new TraceMetricBuilder();
        var pumpCaptures = new TraceMetricBuilder();
        var pumpActions = new TraceMetricBuilder();
        var capture = new TraceMetricBuilder();
        var workerProcessing = new TraceMetricBuilder();
        var action = new TraceMetricBuilder();
        var endToEnd = new TraceMetricBuilder();
        var replayRecordEncoding = new TraceMetricBuilder();
        var starts = new Dictionary<TraceCycleKey, long>();
        var firstTimestamp = long.MaxValue;
        var lastTimestamp = 0L;

        var semantic = artifact.SemanticTrace;
        for (var index = 0; index < semantic.Count; index++)
        {
            var item = semantic[index];
            var payload = item.Payload;
            if ((payload.Fields & ServiceCycleSemanticFields.Timestamp) != 0)
            {
                firstTimestamp = Math.Min(firstTimestamp, payload.TimestampTicks);
                lastTimestamp = Math.Max(lastTimestamp, payload.TimestampTicks);
            }

            switch (item.Kind)
            {
                case ServiceCycleSemanticEventKind.PumpCompleted:
                    pump.AddTicks(payload.TotalDurationTicks);
                    pumpResponses.AddTicks(payload.ResponseDurationTicks);
                    pumpCaptures.AddTicks(payload.CaptureDurationTicks);
                    pumpActions.AddTicks(payload.ActionDurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.CaptureStarted:
                    starts[new TraceCycleKey(in payload)] = payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.CaptureCompleted:
                case ServiceCycleSemanticEventKind.CaptureUnavailable:
                case ServiceCycleSemanticEventKind.CaptureFaulted:
                    capture.AddTicks(payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.EvaluationCompleted:
                case ServiceCycleSemanticEventKind.EvaluationFaulted:
                case ServiceCycleSemanticEventKind.EvaluationDeferred:
                    workerProcessing.AddTicks(payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.ActionCommitted:
                case ServiceCycleSemanticEventKind.ActionRejected:
                case ServiceCycleSemanticEventKind.ActionFaulted:
                    action.AddTicks(payload.DurationTicks);
                    result.RecordAction(item.Kind, in payload);
                    break;
                case ServiceCycleSemanticEventKind.BatchCompleted:
                case ServiceCycleSemanticEventKind.BatchAborted:
                case ServiceCycleSemanticEventKind.BatchOrphaned:
                    var key = new TraceCycleKey(in payload);
                    if (starts.TryGetValue(key, out var startedAt) && payload.TimestampTicks >= startedAt)
                        endToEnd.AddTicks(payload.TimestampTicks - startedAt);
                    break;
            }
        }

        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var footer = artifact.GetCycle(index).Footer;
            replayRecordEncoding.AddMilliseconds(
                footer.EncodingDurationTicks * 1000d / footer.EncodingTimestampFrequency);
            result.ReplayRecordEncodingAllocatedBytes = SaturatingAdd(
                result.ReplayRecordEncodingAllocatedBytes,
                footer.EncodingAllocatedBytes);
        }

        result.WindowMilliseconds = firstTimestamp == long.MaxValue
            ? 0
            : TraceMetric.ToMilliseconds(lastTimestamp - firstTimestamp);
        result.Pump = pump.Freeze();
        result.PumpResponses = pumpResponses.Freeze();
        result.PumpCaptures = pumpCaptures.Freeze();
        result.PumpActions = pumpActions.Freeze();
        result.Capture = capture.Freeze();
        result.WorkerProcessing = workerProcessing.Freeze();
        result.Action = action.Freeze();
        result.EndToEnd = endToEnd.Freeze();
        result.ReplayRecordEncoding = replayRecordEncoding.Freeze();
        return result;
    }

    private void RecordAction(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        switch (kind)
        {
            case ServiceCycleSemanticEventKind.ActionCommitted: CommittedActions++; break;
            case ServiceCycleSemanticEventKind.ActionRejected: RejectedActions++; break;
            case ServiceCycleSemanticEventKind.ActionFaulted: FaultedActions++; break;
        }
        NativeCalls = SaturatingAdd(NativeCalls, payload.NativeCallsAttempted);
        MutationAttempts = SaturatingAdd(MutationAttempts, payload.MutationAttempts);
        MutationsCommitted = SaturatingAdd(MutationsCommitted, payload.MutationsCommitted);
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;
}

internal readonly record struct TraceCycleKey(
    ulong Service,
    ulong Lifecycle,
    ulong Configuration,
    ulong Capture,
    ulong Cycle)
{
    internal TraceCycleKey(in ServiceCycleSemanticPayload payload)
        : this(
            payload.Service,
            payload.Lifecycle,
            payload.Configuration,
            payload.Capture,
            payload.Cycle)
    {
    }
}

internal readonly struct TraceMetric
{
    internal TraceMetric(long samples, double totalMilliseconds, double maximumMilliseconds)
    {
        Samples = samples;
        TotalMilliseconds = totalMilliseconds;
        MaximumMilliseconds = maximumMilliseconds;
    }

    internal long Samples { get; }
    internal double TotalMilliseconds { get; }
    internal double AverageMilliseconds => Samples == 0 ? 0 : TotalMilliseconds / Samples;
    internal double MaximumMilliseconds { get; }
    internal static double ToMilliseconds(long timeSpanTicks) => timeSpanTicks / 10_000d;
}

internal sealed class TraceMetricBuilder
{
    private long _samples;
    private double _total;
    private double _maximum;

    internal void AddTicks(long ticks)
        => AddMilliseconds(TraceMetric.ToMilliseconds(ticks));

    internal void AddMilliseconds(double milliseconds)
    {
        _samples++;
        _total += milliseconds;
        _maximum = Math.Max(_maximum, milliseconds);
    }

    internal TraceMetric Freeze() => new(_samples, _total, _maximum);
}
