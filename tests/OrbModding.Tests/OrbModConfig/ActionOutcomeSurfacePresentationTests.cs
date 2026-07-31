using System;
using System.Linq;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ActionOutcomeSurfacePresentationTests
{
    [Fact]
    public void ReleaseRowsUseExactCalmOutcomeWordsAndExplicitlyExcludeSourceServices()
    {
        var presentation = ActionOutcomeSurfacePresentation.Build(
            new[]
            {
                Outcome("orbautomata.world-collection", ServiceShape.Source, 1, 1, 0, 0, 0),
                Outcome("orbautomata.auto-items", ServiceShape.Ordinary, 0, 0, 0, 0, 0),
                Outcome("orbautomata.auto-scribe", ServiceShape.Ordinary, 1, 0, 0, 0, 1),
                Outcome("orbautomata.auto-harvest", ServiceShape.Ordinary, 3, 2, 1, 0, 0),
                Outcome("orbautomata.auto-buy", ServiceShape.Ordinary, 0, 0, 0, 0, 0),
                Outcome("orbautomata.spell-level", ServiceShape.Ordinary, 1, 0, 1, 0, 0),
                Outcome("orbautomata.auto-cast", ServiceShape.Ordinary, 1, 0, 0, 1, 0),
                Outcome("orbautomata.auto-concept", ServiceShape.Ordinary, 1, 0, 0, 1, 0),
                Outcome("orbmentor.mastery-sharing", ServiceShape.Ordinary, 1, 1, 0, 0, 0),
            },
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        Assert.Equal(ActionOutcomeSurfacePresentation.Title, "Recent automation activity");
        Assert.Equal(
            new[]
            {
                "Auto Items",
                "Auto Scribe",
                "Auto Harvest",
                "Auto Buy",
                "Spell Leveling",
                "Auto Cast",
                "Auto Concept",
                "Mentor",
            },
            presentation.Rows.Select(row => row.DisplayName));
        Assert.Equal(
            new[]
            {
                "○ Waiting",
                "! Needs attention",
                "★ Completed  ·  – Skipped",
                "○ Waiting",
                "– Skipped",
                "○ Not completed",
                "○ Not completed",
                "★ Completed",
            },
            presentation.Rows.Select(row => row.Summary));
        Assert.All(presentation.Rows, row => Assert.Equal(string.Empty, row.Detail));
        Assert.DoesNotContain(
            presentation.Rows,
            row => string.Equals(row.DisplayName, "World collection", StringComparison.Ordinal));
    }

    [Fact]
    public void WaitingCompletedRejectedAndFaultedRemainVisiblyDistinct()
    {
        var presentation = ActionOutcomeSurfacePresentation.Build(
            new[]
            {
                Outcome("orbautomata.auto-buy", ServiceShape.Ordinary, 0, 0, 0, 0, 0),
                Outcome("orbautomata.auto-harvest", ServiceShape.Ordinary, 1, 1, 0, 0, 0),
                Outcome("orbautomata.auto-concept", ServiceShape.Ordinary, 1, 0, 0, 1, 0),
                Outcome("orbautomata.auto-scribe", ServiceShape.Ordinary, 1, 0, 0, 0, 1),
            },
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        Assert.Equal(
            new[]
            {
                ActionOutcomeTone.Waiting,
                ActionOutcomeTone.Completed,
                ActionOutcomeTone.QuietIssue,
                ActionOutcomeTone.Faulted,
            },
            presentation.Rows.Select(row => row.Tone));
        Assert.Equal(new long[] { 0, 1, 0, 0 }, presentation.Rows.Select(row => row.Committed));
        Assert.Equal(new long[] { 0, 0, 1, 0 }, presentation.Rows.Select(row => row.Rejected));
        Assert.Equal(new long[] { 0, 0, 0, 1 }, presentation.Rows.Select(row => row.Faulted));
    }

    [Fact]
    public void ReleaseCopyNeverTurnsPlannedOrRejectedWorkIntoACompletionClaim()
    {
        var presentation = ActionOutcomeSurfacePresentation.Build(
            new[]
            {
                Outcome("orbautomata.auto-buy", ServiceShape.Ordinary, 9, 0, 0, 0, 0),
                Outcome("orbautomata.auto-concept", ServiceShape.Ordinary, 9, 0, 0, 1, 0),
            },
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        Assert.Equal("○ Waiting", presentation.Rows[0].Summary);
        Assert.Equal("○ Not completed", presentation.Rows[1].Summary);
        Assert.All(presentation.Rows, row =>
        {
            Assert.DoesNotContain("9", row.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("planned", row.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("committed", row.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code", row.Summary, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SlimTimingLineReplacesEveryOldChartType()
    {
        var presentation = ActionOutcomeSurfacePresentation.Build(
            ReadOnlySpan<ServiceActionOutcomeSnapshot>.Empty,
            new[] { Timing(0.125), Timing(0.500) });
        var assembly = typeof(ActionOutcomeSurfacePresentation).Assembly;

        Assert.Equal(
            "Recent processing · average 0.312 ms · worst 0.500 ms",
            presentation.TimingSummary);
        Assert.Equal(
            ActionOutcomeSurfacePresentation.EmptyTiming,
            ActionOutcomeSurfacePresentation.Build(
                ReadOnlySpan<ServiceActionOutcomeSnapshot>.Empty,
                ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty).TimingSummary);
        Assert.NotNull(assembly.GetType("OrbModConfig.ActionOutcomeView"));
        Assert.Null(assembly.GetType("OrbModConfig.PumpTimingGraphView"));
        Assert.Null(assembly.GetType("OrbModConfig.PumpTimingGraphProjection"));
        Assert.Null(assembly.GetType("OrbModConfig.PumpTimingGraphGraphic"));
    }

    private static ServiceActionOutcomeSnapshot Outcome(
        string service,
        ServiceShape shape,
        long planned,
        long committed,
        long skipped,
        long rejected,
        long faulted) => new(
        new ServiceId(service),
        shape,
        observationCount: planned == 0 ? 0 : 1,
        planned,
        committed,
        skipped,
        rejected,
        faulted,
        default);

    private static ServiceCyclePumpTimingSample Timing(double milliseconds)
    {
        var report = new SuiteFramePumpReport(
            frameIdentity: 1,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: 0,
            cyclesStarted: 0,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecyclePositionTransitions: 0,
            responseDuration: default,
            actionDuration: default,
            captureDuration: default,
            totalDuration: MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)));
        return new ServiceCyclePumpTimingSample(in report);
    }
}
