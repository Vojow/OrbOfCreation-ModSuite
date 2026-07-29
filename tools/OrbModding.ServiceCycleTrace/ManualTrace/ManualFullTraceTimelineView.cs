using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal static class ManualFullTraceTimelineView
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        writer.WriteLine("## Worker and service timeline");
        writer.WriteLine();
        writer.WriteLine(
            "This is a causal semantic-phase view, not physical thread scheduling evidence. Pump records are summarized above.");
        writer.WriteLine();
        var wroteEvent = false;
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted) continue;
            wroteEvent = true;
            writer.Write("- #");
            writer.Write(item.Id.Sequence.ToString(CultureInfo.InvariantCulture));
            writer.Write(" +");
            writer.Write(OffsetMilliseconds(item.Payload.TimestampTicks, session.Document.FirstTimestampTicks));
            writer.Write(" ms — ");
            writer.Write(item.Kind);
            var detail = ServiceCycleTraceTimeline.Describe(in item);
            if (detail.Length != 0)
            {
                writer.Write(": ");
                writer.Write(detail);
            }
            if (item.HasParent)
            {
                writer.Write("; parent=#");
                writer.Write(item.Parent.Sequence.ToString(CultureInfo.InvariantCulture));
            }
            writer.WriteLine();
        }
        if (!wroteEvent) writer.WriteLine("No non-pump semantic events were recorded.");
    }

    private static string OffsetMilliseconds(long timestamp, long origin) =>
        TraceMetric.ToMilliseconds(timestamp >= origin ? timestamp - origin : 0)
            .ToString("F3", CultureInfo.InvariantCulture);
}
