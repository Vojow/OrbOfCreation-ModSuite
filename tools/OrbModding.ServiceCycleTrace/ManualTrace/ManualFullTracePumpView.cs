using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal static class ManualFullTracePumpView
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        var summary = Summarize(session);
        writer.WriteLine("## Pump view");
        writer.WriteLine();
        writer.WriteLine($"- Accepted pumps: {summary.Accepted.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Rejected duplicate pumps: {summary.Rejected.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Otherwise idle accepted pumps: {summary.Idle.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Active accepted pumps: {summary.Active.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Responses / captures / actions: {summary.Responses.ToString("N0", CultureInfo.InvariantCulture)} / {summary.Captures.ToString("N0", CultureInfo.InvariantCulture)} / {summary.Actions.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- Cycles started / world-gate holds: {summary.Started.ToString("N0", CultureInfo.InvariantCulture)} / {summary.Held.ToString("N0", CultureInfo.InvariantCulture)}");
        writer.WriteLine();
        writer.WriteLine("| Phase | Samples | Total ms | Average ms | Max ms |");
        writer.WriteLine("|---|---:|---:|---:|---:|");
        WriteMetric(writer, "Whole pump", summary.Pump.Freeze());
        WriteMetric(writer, "Response phase", summary.Response.Freeze());
        WriteMetric(writer, "Capture phase", summary.Capture.Freeze());
        WriteMetric(writer, "Action phase", summary.Action.Freeze());
        writer.WriteLine();
        writer.WriteLine("Phase rows are contained within the whole-pump row and are not additive.");
        writer.WriteLine();
        if (summary.Active + summary.Rejected == 0) return;

        writer.WriteLine("### Active or rejected pumps");
        writer.WriteLine();
        writer.WriteLine("| Offset ms | Frame | Accepted | Start ordinal | Responses | Captures | Actions | Started | Held | Lifecycle transitions | Pump ms |");
        writer.WriteLine("|---:|---:|:---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted ||
                item.Payload.PumpAccepted && !ServiceCycleTraceTimeline.HasPumpActivity(item.Payload))
                continue;
            var payload = item.Payload;
            writer.Write("| ");
            writer.Write(OffsetMilliseconds(payload.TimestampTicks, session.Document.FirstTimestampTicks));
            writer.Write(" | ");
            writer.Write(payload.FrameIdentity.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.PumpAccepted ? "yes" : "no");
            writer.Write(" | ");
            writer.Write(payload.StartingOrdinal.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.ResponsesAcquired.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.CapturesAttempted.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.ActionsAttempted.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.CyclesStarted.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.WorldGateDeferrals.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(payload.LifecycleTransitions.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(TraceMetric.ToMilliseconds(payload.TotalDurationTicks).ToString("F3", CultureInfo.InvariantCulture));
            writer.WriteLine(" |");
        }
        writer.WriteLine();
    }

    private static PumpSummary Summarize(ManualFullTraceSession session)
    {
        var result = new PumpSummary();
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted) continue;
            var payload = item.Payload;
            if (payload.PumpAccepted)
            {
                result.Accepted++;
                if (ServiceCycleTraceTimeline.HasPumpActivity(payload)) result.Active++;
                else result.Idle++;
            }
            else
            {
                result.Rejected++;
            }
            result.Responses += payload.ResponsesAcquired;
            result.Captures += payload.CapturesAttempted;
            result.Actions += payload.ActionsAttempted;
            result.Started += payload.CyclesStarted;
            result.Held += payload.WorldGateDeferrals;
            result.Pump.AddTicks(payload.TotalDurationTicks);
            result.Response.AddTicks(payload.ResponseDurationTicks);
            result.Capture.AddTicks(payload.CaptureDurationTicks);
            result.Action.AddTicks(payload.ActionDurationTicks);
        }
        return result;
    }

    private static void WriteMetric(TextWriter writer, string name, TraceMetric metric)
    {
        writer.Write("| ");
        writer.Write(name);
        writer.Write(" | ");
        writer.Write(metric.Samples.ToString("N0", CultureInfo.InvariantCulture));
        writer.Write(" | ");
        writer.Write(metric.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        writer.Write(" | ");
        writer.Write(metric.AverageMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        writer.Write(" | ");
        writer.Write(metric.MaximumMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
        writer.WriteLine(" |");
    }

    private static string OffsetMilliseconds(long timestamp, long origin) =>
        TraceMetric.ToMilliseconds(timestamp >= origin ? timestamp - origin : 0)
            .ToString("F3", CultureInfo.InvariantCulture);

    private sealed class PumpSummary
    {
        internal long Accepted;
        internal long Rejected;
        internal long Idle;
        internal long Active;
        internal long Responses;
        internal long Captures;
        internal long Actions;
        internal long Started;
        internal long Held;
        internal TraceMetricBuilder Pump { get; } = new();
        internal TraceMetricBuilder Response { get; } = new();
        internal TraceMetricBuilder Capture { get; } = new();
        internal TraceMetricBuilder Action { get; } = new();
    }
}
