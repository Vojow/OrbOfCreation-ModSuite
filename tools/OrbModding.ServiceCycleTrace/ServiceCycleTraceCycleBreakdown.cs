using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace;

internal readonly struct ServiceCycleTraceCycleBreakdown
{
    private ServiceCycleTraceCycleBreakdown(
        double? queueWaitMilliseconds,
        double? workerMilliseconds,
        double? publishToActionMilliseconds,
        double? actionMilliseconds,
        double? endToEndMilliseconds,
        string outcome)
    {
        QueueWaitMilliseconds = queueWaitMilliseconds;
        WorkerMilliseconds = workerMilliseconds;
        PublishToActionMilliseconds = publishToActionMilliseconds;
        ActionMilliseconds = actionMilliseconds;
        EndToEndMilliseconds = endToEndMilliseconds;
        Outcome = outcome;
    }

    internal double? QueueWaitMilliseconds { get; }
    internal double? WorkerMilliseconds { get; }
    internal double? PublishToActionMilliseconds { get; }
    internal double? ActionMilliseconds { get; }
    internal double? EndToEndMilliseconds { get; }
    internal string Outcome { get; }

    internal static ServiceCycleTraceCycleBreakdown Create(ServiceCycleReplayArtifactCycle cycle)
    {
        if (!cycle.IsComplete)
            return new ServiceCycleTraceCycleBreakdown(
                null,
                null,
                null,
                null,
                null,
                "Unavailable (" + IncompleteReason(cycle) + ")");

        long? captureStarted = null;
        long? queued = null;
        long? workerStarted = null;
        long? published = null;
        long? actionAttempted = null;
        long? batchTerminal = null;
        double? worker = null;
        double? action = null;
        var outcome = "Incomplete";

        for (var index = 0; index < cycle.SemanticEventCount; index++)
        {
            var item = cycle.GetSemanticEvent(index);
            var payload = item.Payload;
            switch (item.Kind)
            {
                case ServiceCycleSemanticEventKind.CaptureStarted:
                    captureStarted = payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.CycleQueued:
                    queued = payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.CycleStarted:
                    workerStarted = payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.EvaluationCompleted:
                case ServiceCycleSemanticEventKind.EvaluationFaulted:
                case ServiceCycleSemanticEventKind.EvaluationDeferred:
                    worker = Milliseconds(payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.BatchPublished:
                    published = payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.ActionAttempted:
                    actionAttempted ??= payload.TimestampTicks;
                    break;
                case ServiceCycleSemanticEventKind.ActionCommitted:
                    action = Add(action, payload.DurationTicks);
                    outcome = "Committed";
                    break;
                case ServiceCycleSemanticEventKind.ActionRejected:
                    action = Add(action, payload.DurationTicks);
                    outcome = "Rejected";
                    break;
                case ServiceCycleSemanticEventKind.ActionFaulted:
                    action = Add(action, payload.DurationTicks);
                    outcome = "Faulted";
                    break;
                case ServiceCycleSemanticEventKind.BatchCompleted:
                    batchTerminal = payload.TimestampTicks;
                    if (outcome == "Incomplete") outcome = "Completed";
                    break;
                case ServiceCycleSemanticEventKind.BatchAborted:
                    batchTerminal = payload.TimestampTicks;
                    if (outcome == "Incomplete") outcome = "Aborted";
                    break;
                case ServiceCycleSemanticEventKind.BatchOrphaned:
                    batchTerminal = payload.TimestampTicks;
                    if (outcome == "Incomplete") outcome = "Orphaned";
                    break;
            }
        }

        return new ServiceCycleTraceCycleBreakdown(
            Difference(queued, workerStarted),
            worker,
            Difference(published, actionAttempted),
            action,
            Difference(captureStarted, batchTerminal),
            outcome);
    }

    private static double? Difference(long? started, long? completed) =>
        started.HasValue && completed.HasValue && completed.Value >= started.Value
            ? Milliseconds(completed.Value - started.Value)
            : null;

    private static double Add(double? total, long ticks) => (total ?? 0) + Milliseconds(ticks);
    private static double Milliseconds(long ticks) => TraceMetric.ToMilliseconds(ticks);

    private static string IncompleteReason(ServiceCycleReplayArtifactCycle cycle)
    {
        if (cycle.Join.Code != ServiceCycleReplaySemanticJoinCode.Complete)
            return cycle.Join.Code.ToString();
        if (!cycle.Footer.Completeness.IsComplete) return cycle.Footer.Completeness.Code.ToString();
        return cycle.Footer.Disposition.ToString();
    }
}
