using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal static class ManualFullTraceServiceView
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        writer.WriteLine("## Service view");
        writer.WriteLine();
        writer.WriteLine(
            "Format v1 carries numeric service identities only; this report does not infer feature names.");
        writer.WriteLine();
        var services = Summarize(session);
        if (services.Count == 0)
        {
            writer.WriteLine("No service-scoped events were recorded.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine("| Service | Events | Cycles queued / completed / faulted-or-orphaned | Evaluations completed / deferred / faulted | Projection faults | Actions committed / rejected / faulted | Avg capture ms | Avg worker ms | Avg action ms |");
        writer.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var pair in services.OrderBy(item => item.Key))
        {
            var item = pair.Value;
            writer.Write("| ");
            writer.Write(pair.Key.ToString(CultureInfo.InvariantCulture));
            writer.Write(" | ");
            writer.Write(item.Events.ToString("N0", CultureInfo.InvariantCulture));
            writer.Write(" | ");
            WriteTriple(writer, item.CyclesQueued, item.CyclesCompleted, item.CyclesFaulted);
            writer.Write(" | ");
            WriteTriple(writer, item.EvaluationsCompleted, item.EvaluationsDeferred, item.EvaluationsFaulted);
            writer.Write(" | ");
            writer.Write(item.ProjectionFaults.ToString("N0", CultureInfo.InvariantCulture));
            writer.Write(" | ");
            WriteTriple(writer, item.ActionsCommitted, item.ActionsRejected, item.ActionsFaulted);
            writer.Write(" | ");
            WriteAverage(writer, item.Capture.Freeze());
            writer.Write(" | ");
            WriteAverage(writer, item.Worker.Freeze());
            writer.Write(" | ");
            WriteAverage(writer, item.Action.Freeze());
            writer.WriteLine(" |");
        }
        writer.WriteLine();
    }

    private static Dictionary<ulong, ServiceSummary> Summarize(ManualFullTraceSession session)
    {
        var result = new Dictionary<ulong, ServiceSummary>();
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            if ((item.Payload.Fields & ServiceCycleSemanticFields.Service) == 0 || item.Payload.Service == 0)
                continue;
            if (!result.TryGetValue(item.Payload.Service, out var summary))
            {
                summary = new ServiceSummary();
                result.Add(item.Payload.Service, summary);
            }
            summary.Record(in item);
        }
        return result;
    }

    private static void WriteTriple(TextWriter writer, long first, long second, long third) =>
        writer.Write($"{first.ToString("N0", CultureInfo.InvariantCulture)} / {second.ToString("N0", CultureInfo.InvariantCulture)} / {third.ToString("N0", CultureInfo.InvariantCulture)}");

    private static void WriteAverage(TextWriter writer, TraceMetric metric) =>
        writer.Write(metric.Samples == 0
            ? "—"
            : metric.AverageMilliseconds.ToString("F3", CultureInfo.InvariantCulture));

    private sealed class ServiceSummary
    {
        internal long Events;
        internal long CyclesQueued;
        internal long CyclesCompleted;
        internal long CyclesFaulted;
        internal long EvaluationsCompleted;
        internal long EvaluationsDeferred;
        internal long EvaluationsFaulted;
        internal long ProjectionFaults;
        internal long ActionsCommitted;
        internal long ActionsRejected;
        internal long ActionsFaulted;
        internal TraceMetricBuilder Capture { get; } = new();
        internal TraceMetricBuilder Worker { get; } = new();
        internal TraceMetricBuilder Action { get; } = new();

        internal void Record(in ServiceCycleSemanticEvent item)
        {
            Events++;
            switch (item.Kind)
            {
                case ServiceCycleSemanticEventKind.CycleQueued: CyclesQueued++; break;
                case ServiceCycleSemanticEventKind.CycleCompleted: CyclesCompleted++; break;
                case ServiceCycleSemanticEventKind.CycleFaulted:
                case ServiceCycleSemanticEventKind.CycleOrphaned: CyclesFaulted++; break;
                case ServiceCycleSemanticEventKind.CaptureCompleted:
                case ServiceCycleSemanticEventKind.CaptureUnavailable:
                case ServiceCycleSemanticEventKind.CaptureFaulted:
                    Capture.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.EvaluationCompleted:
                    EvaluationsCompleted++;
                    Worker.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.EvaluationDeferred:
                    EvaluationsDeferred++;
                    Worker.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.EvaluationFaulted:
                    EvaluationsFaulted++;
                    Worker.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.ProjectionFaulted:
                    ProjectionFaults++;
                    break;
                case ServiceCycleSemanticEventKind.ActionCommitted:
                    ActionsCommitted++;
                    Action.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.ActionRejected:
                    ActionsRejected++;
                    Action.AddTicks(item.Payload.DurationTicks);
                    break;
                case ServiceCycleSemanticEventKind.ActionFaulted:
                    ActionsFaulted++;
                    Action.AddTicks(item.Payload.DurationTicks);
                    break;
            }
        }
    }
}
