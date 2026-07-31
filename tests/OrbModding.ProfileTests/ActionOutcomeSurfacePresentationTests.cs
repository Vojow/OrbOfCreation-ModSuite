using System;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ActionOutcomeSurfacePresentationTests
{
    [Fact]
    public void PerformanceDebugAddsExactCountsAndLastBoundaryReason()
    {
        var outcome = new ServiceActionOutcomeSnapshot(
            new ServiceId("orbautomata.auto-buy"),
            ServiceShape.Ordinary,
            observationCount: 3,
            planned: 4,
            committed: 2,
            skipped: 1,
            rejected: 1,
            faulted: 0,
            new ServiceActionOutcomeBoundary(
                ServiceActionOutcomeBoundaryKind.Rejected,
                CommonActionResultCodes.PolicyRejected.Value));

        var presentation = ActionOutcomeSurfacePresentation.Build(
            new[] { outcome },
            ReadOnlySpan<ServiceCyclePumpTimingSample>.Empty);

        var row = Assert.Single(presentation.Rows);
        Assert.Equal("Auto Buy", row.DisplayName);
        Assert.Equal("★ Completed  ·  – Skipped  ·  ○ Not completed", row.Summary);
        Assert.Equal(
            "planned 4 · committed 2 · skipped 1 · rejected 1 · faulted 0 · last rejected (6)",
            row.Detail);
        Assert.Equal(ActionOutcomeTone.Completed, row.Tone);
    }

    [Fact]
    public void PerformanceDebugKeepsSourceExclusionAndSlimTimingComposition()
    {
        var source = new ServiceActionOutcomeSnapshot(
            new ServiceId("orbautomata.world-collection"),
            ServiceShape.Source,
            observationCount: 1,
            planned: 1,
            committed: 1,
            skipped: 0,
            rejected: 0,
            faulted: 0,
            default);
        var idle = new ServiceActionOutcomeSnapshot(
            new ServiceId("orbautomata.auto-cast"),
            ServiceShape.Ordinary,
            observationCount: 0,
            planned: 0,
            committed: 0,
            skipped: 0,
            rejected: 0,
            faulted: 0,
            default);
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
            totalDuration: MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(0.250)));
        var timing = new ServiceCyclePumpTimingSample(in report);

        var presentation = ActionOutcomeSurfacePresentation.Build(
            new[] { source, idle },
            new[] { timing });

        var row = Assert.Single(presentation.Rows);
        Assert.Equal("Auto Cast", row.DisplayName);
        Assert.Equal("○ Waiting", row.Summary);
        Assert.Equal(
            "planned 0 · committed 0 · skipped 0 · rejected 0 · faulted 0 · last none",
            row.Detail);
        Assert.Equal(
            "Recent processing · average 0.250 ms · worst 0.250 ms",
            presentation.TimingSummary);
        Assert.Null(typeof(ActionOutcomeSurfacePresentation).Assembly.GetType(
            "OrbModConfig.PumpTimingGraphView"));
    }
}
