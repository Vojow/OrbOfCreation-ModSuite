using System;
using System.Globalization;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

namespace OrbModConfig;

internal enum ActionOutcomeTone
{
    Waiting = 0,
    Completed = 1,
    QuietIssue = 2,
    Faulted = 3,
}

internal readonly struct ActionOutcomeRowPresentation
{
    internal ActionOutcomeRowPresentation(
        string displayName,
        string summary,
        string detail,
        ActionOutcomeTone tone,
        long committed,
        long skipped,
        long rejected,
        long faulted)
    {
        DisplayName = displayName;
        Summary = summary;
        Detail = detail;
        Tone = tone;
        Committed = committed;
        Skipped = skipped;
        Rejected = rejected;
        Faulted = faulted;
    }

    internal string DisplayName { get; }
    internal string Summary { get; }
    internal string Detail { get; }
    internal ActionOutcomeTone Tone { get; }
    internal long Committed { get; }
    internal long Skipped { get; }
    internal long Rejected { get; }
    internal long Faulted { get; }
}

internal readonly struct ActionOutcomeSurfacePresentation
{
    internal const string Title = "Recent automation activity";
    internal const string Waiting = "○ Waiting";
    internal const string Completed = "★ Completed";
    internal const string Skipped = "– Skipped";
    internal const string Rejected = "○ Not completed";
    internal const string Faulted = "! Needs attention";
    internal const string EmptyTiming = "Recent processing · waiting for activity";

    internal ActionOutcomeSurfacePresentation(
        ActionOutcomeRowPresentation[] rows,
        string timingSummary)
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        TimingSummary = timingSummary ?? throw new ArgumentNullException(nameof(timingSummary));
    }

    internal ActionOutcomeRowPresentation[] Rows { get; }
    internal string TimingSummary { get; }

    internal static ActionOutcomeSurfacePresentation Build(
        ReadOnlySpan<ServiceActionOutcomeSnapshot> outcomes,
        ReadOnlySpan<ServiceCyclePumpTimingSample> timings)
    {
        var automationCount = 0;
        for (var index = 0; index < outcomes.Length; index++)
            if (outcomes[index].Shape == ServiceShape.Ordinary) automationCount++;

        var rows = new ActionOutcomeRowPresentation[automationCount];
        var written = 0;
        for (var index = 0; index < outcomes.Length; index++)
        {
            var outcome = outcomes[index];
            if (outcome.Shape != ServiceShape.Ordinary) continue;
            rows[written++] = Row(in outcome);
        }
        return new ActionOutcomeSurfacePresentation(rows, Timing(timings));
    }

    private static ActionOutcomeRowPresentation Row(in ServiceActionOutcomeSnapshot outcome)
    {
        var name = AutomataServiceCycleTraceRoster.DisplayName(outcome.Service);
        if (string.IsNullOrEmpty(name)) name = outcome.Service.Value;
        var summary = Summary(in outcome);
        var tone = outcome.Faulted > 0
            ? ActionOutcomeTone.Faulted
            : outcome.Committed > 0
                ? ActionOutcomeTone.Completed
                : outcome.Skipped > 0 || outcome.Rejected > 0
                    ? ActionOutcomeTone.QuietIssue
                    : ActionOutcomeTone.Waiting;
#if SERVICE_CYCLE_PROFILE
        var detail = string.Format(
            CultureInfo.InvariantCulture,
            "planned {0} · committed {1} · skipped {2} · rejected {3} · faulted {4} · last {5}",
            outcome.Planned,
            outcome.Committed,
            outcome.Skipped,
            outcome.Rejected,
            outcome.Faulted,
            Boundary(outcome.LastBoundary));
#else
        const string detail = "";
#endif
        return new ActionOutcomeRowPresentation(
            name,
            summary,
            detail,
            tone,
            outcome.Committed,
            outcome.Skipped,
            outcome.Rejected,
            outcome.Faulted);
    }

    private static string Summary(in ServiceActionOutcomeSnapshot outcome)
    {
        var text = string.Empty;
        Append(ref text, outcome.Committed > 0, Completed);
        Append(ref text, outcome.Skipped > 0, Skipped);
        Append(ref text, outcome.Rejected > 0, Rejected);
        Append(ref text, outcome.Faulted > 0, Faulted);
        return text.Length == 0 ? Waiting : text;
    }

    private static void Append(ref string text, bool include, string value)
    {
        if (!include) return;
        text = text.Length == 0 ? value : text + "  ·  " + value;
    }

    private static string Timing(ReadOnlySpan<ServiceCyclePumpTimingSample> timings)
    {
        if (timings.Length == 0) return EmptyTiming;
        long total = 0;
        long worst = 0;
        for (var index = 0; index < timings.Length; index++)
        {
            var ticks = timings[index].TotalDuration.Ticks;
            total = ticks > long.MaxValue - total ? long.MaxValue : total + ticks;
            if (ticks > worst) worst = ticks;
        }
        return string.Format(
            CultureInfo.InvariantCulture,
            "Recent processing · average {0:F3} ms · worst {1:F3} ms",
            total / (double)timings.Length / TimeSpan.TicksPerMillisecond,
            worst / (double)TimeSpan.TicksPerMillisecond);
    }

#if SERVICE_CYCLE_PROFILE
    private static string Boundary(ServiceActionOutcomeBoundary boundary)
    {
        var reason = boundary.Kind switch
        {
            ServiceActionOutcomeBoundaryKind.Waiting => "waiting",
            ServiceActionOutcomeBoundaryKind.Committed => "committed",
            ServiceActionOutcomeBoundaryKind.Skipped => "skipped",
            ServiceActionOutcomeBoundaryKind.Rejected => "rejected",
            ServiceActionOutcomeBoundaryKind.Faulted => "faulted",
            ServiceActionOutcomeBoundaryKind.LifecycleChanged => "lifecycle changed",
            ServiceActionOutcomeBoundaryKind.WorldGateHeld => "waiting for a newer world",
            ServiceActionOutcomeBoundaryKind.EmergencyStopped => "emergency stopped",
            _ => "none",
        };
        return boundary.Code == 0
            ? reason
            : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", reason, boundary.Code);
    }
#endif
}
