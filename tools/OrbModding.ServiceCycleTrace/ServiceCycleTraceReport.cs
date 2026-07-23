using System.Globalization;
using System.Text;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.Profiles;

namespace OrbModding.ServiceCycleTrace;

public static class ServiceCycleTraceReport
{
    public static string Render(
        string artifactName,
        ServiceCycleReplayArtifactDocument artifact,
        ServiceCycleTraceProfile profile = ServiceCycleTraceProfile.Generic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        ArgumentNullException.ThrowIfNull(artifact);
        var featureProfile = ServiceCycleTraceProfiles.BindSelected(profile, artifact);
        var summary = ServiceCycleTraceSummary.Create(artifact);
        var builder = new StringBuilder(4096);
        builder.AppendLine("# ServiceCycle trace report");
        builder.AppendLine();
        builder.AppendLine($"- Artifact: `{Path.GetFileName(artifactName)}`");
        builder.AppendLine($"- Schema: {artifact.SchemaVersion}");
        builder.AppendLine($"- Eligibility: {artifact.Eligibility}");
        builder.AppendLine($"- Completeness: {artifact.Completeness.Code}");
        builder.AppendLine($"- Cycles: {artifact.CycleCount}");
        builder.AppendLine($"- Semantic events: {artifact.SemanticTrace.Count}");
        builder.AppendLine($"- Dropped semantic events: {artifact.SemanticTrace.Dropped.Count}");
        if (featureProfile is not null)
            builder.AppendLine($"- Feature profile: {featureProfile.DisplayName} (explicitly selected)");
        builder.Append("- Observed window: ");
        builder.Append(summary.WindowMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.AppendLine(" ms");
        builder.AppendLine();
        builder.AppendLine("## Timing");
        builder.AppendLine();
        builder.AppendLine("These timings have different scopes and are not additive.");
        builder.AppendLine();
        builder.AppendLine("### Unity main thread");
        builder.AppendLine();
        builder.AppendLine("| Work | Samples | Total ms | Average ms | Max ms |");
        builder.AppendLine("|---|---:|---:|---:|---:|");
        AppendMetric(builder, "Main-thread pump", summary.Pump);
        AppendMetric(builder, "Pump response phase", summary.PumpResponses);
        AppendMetric(builder, "Pump capture phase", summary.PumpCaptures);
        AppendMetric(builder, "Pump action phase", summary.PumpActions);
        builder.AppendLine();
        builder.AppendLine("Pump phase rows are contained within the main-thread pump row.");
        builder.AppendLine();
        builder.AppendLine("### Per-operation main-thread samples");
        builder.AppendLine();
        builder.AppendLine("| Work | Samples | Total ms | Average ms | Max ms |");
        builder.AppendLine("|---|---:|---:|---:|---:|");
        AppendMetric(builder, "Capture attempt", summary.Capture);
        AppendMetric(builder, "Action attempt terminal", summary.Action);
        builder.AppendLine();
        builder.AppendLine(
            "These rows show the same capture and action time grouped by operation instead of by pump.");
        builder.AppendLine();
        builder.AppendLine("### Worker and elapsed time");
        builder.AppendLine();
        builder.AppendLine("| Work | Samples | Total ms | Average ms | Max ms |");
        builder.AppendLine("|---|---:|---:|---:|---:|");
        AppendMetric(builder, "Worker processing through state projection", summary.WorkerProcessing);
        AppendMetric(builder, "Capture-to-batch terminal elapsed", summary.EndToEnd);
        AppendMetric(builder, "Replay record encode/retain subset", summary.ReplayRecordEncoding);
        builder.AppendLine();
        builder.AppendLine(
            "Worker-processing time starts after request dequeue and includes state preparation, evaluation, " +
            "state projection, detached replay record construction, and enabled recording work. It excludes " +
            "response construction and handoff publication.");
        builder.AppendLine();
        builder.AppendLine(
            "Replay record encode/retain is a contained subset of worker-processing time; it excludes detached " +
            "record construction and the cycle-footer append.");
        builder.AppendLine();
        if (featureProfile is not null) AppendFeatureCycles(builder, artifact, featureProfile);
        builder.AppendLine("## Action evidence");
        builder.AppendLine();
        builder.AppendLine($"- Committed: {summary.CommittedActions}");
        builder.AppendLine($"- Rejected: {summary.RejectedActions}");
        builder.AppendLine($"- Faulted: {summary.FaultedActions}");
        builder.AppendLine($"- Native calls / attempts / commits: {summary.NativeCalls} / {summary.MutationAttempts} / {summary.MutationsCommitted}");
        builder.AppendLine(
            $"- Replay record encode/retain allocated bytes: {summary.ReplayRecordEncodingAllocatedBytes}");
        builder.AppendLine();
        builder.AppendLine("Replay record allocation is recording overhead, not total worker or Unity allocation.");
        builder.AppendLine();
        builder.AppendLine("## Timeline");
        builder.AppendLine();
        AppendTimeline(builder, artifact.SemanticTrace);
        return builder.ToString();
    }

    private static void AppendFeatureCycles(
        StringBuilder builder,
        ServiceCycleReplayArtifactDocument artifact,
        IServiceCycleTraceFeatureProfile profile)
    {
        builder.Append("## ");
        builder.Append(profile.DisplayName);
        builder.AppendLine(" cycle timing");
        builder.AppendLine();
        builder.AppendLine(
            "Queue wait ends when worker evaluation starts. Publish-to-action is elapsed wall time from the " +
            "worker's response publication to the Unity main-thread action attempt; it is not publication CPU time.");
        builder.AppendLine();
        builder.AppendLine("| Lifecycle | Cycle | Action | Outcome | Queue wait ms | Worker ms | Publish-to-action ms | Action ms | End-to-end ms |");
        builder.AppendLine("|---:|---:|---|---|---:|---:|---:|---:|---:|");
        for (var index = 0; index < artifact.CycleCount; index++)
        {
            var cycle = artifact.GetCycle(index);
            if (!profile.Includes(cycle)) continue;
            var timing = ServiceCycleTraceCycleBreakdown.Create(cycle);
            builder.Append("| ");
            builder.Append(cycle.Key.Lifecycle.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(cycle.Key.Cycle.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(profile.DescribeAction(cycle));
            builder.Append(" | ");
            builder.Append(timing.Outcome);
            builder.Append(" | ");
            AppendMilliseconds(builder, timing.QueueWaitMilliseconds);
            builder.Append(" | ");
            AppendMilliseconds(builder, timing.WorkerMilliseconds);
            builder.Append(" | ");
            AppendMilliseconds(builder, timing.PublishToActionMilliseconds);
            builder.Append(" | ");
            AppendMilliseconds(builder, timing.ActionMilliseconds);
            builder.Append(" | ");
            AppendMilliseconds(builder, timing.EndToEndMilliseconds);
            builder.AppendLine(" |");
        }
        builder.AppendLine();
        builder.AppendLine(
            "End-to-end is capture start through batch terminal. A dash means that interval has no matching event, " +
            "such as a cycle with no action attempt.");
        builder.AppendLine();
    }

    private static void AppendMetric(StringBuilder builder, string name, TraceMetric metric)
    {
        builder.Append("| ");
        builder.Append(name);
        builder.Append(" | ");
        builder.Append(metric.Samples.ToString(CultureInfo.InvariantCulture));
        builder.Append(" | ");
        builder.Append(metric.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.Append(" | ");
        builder.Append(metric.AverageMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.Append(" | ");
        builder.Append(metric.MaximumMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        builder.AppendLine(" |");
    }

    private static void AppendMilliseconds(StringBuilder builder, double? milliseconds) =>
        builder.Append(milliseconds.HasValue
            ? milliseconds.Value.ToString("F3", CultureInfo.InvariantCulture)
            : "—");

    private static void AppendTimeline(StringBuilder builder, ServiceCycleTraceDocument trace)
    {
        var events = ServiceCycleTraceTimeline.MeaningfulEvents(trace);
        if (events.Count == 0)
        {
            builder.AppendLine("No meaningful events were retained.");
            return;
        }
        var origin = events.Min(item => item.Payload.TimestampTicks);
        foreach (var item in events)
        {
            var offset = item.Payload.TimestampTicks >= origin
                ? TraceMetric.ToMilliseconds(item.Payload.TimestampTicks - origin)
                : 0;
            builder.Append("- +");
            builder.Append(offset.ToString("F3", CultureInfo.InvariantCulture));
            builder.Append(" ms — ");
            builder.Append(item.Kind);
            var detail = ServiceCycleTraceTimeline.Describe(in item);
            if (detail.Length != 0)
            {
                builder.Append(": ");
                builder.Append(detail);
            }
            builder.AppendLine();
        }
    }
}
