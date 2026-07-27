using System;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTraceRosterTests
{
    private static ServiceCycleTraceRoster RosterFor(string displayName) =>
        new(new[]
        {
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.ServiceKind,
                7,
                "orbautomata.auto-harvest",
                displayName),
        });

    [Fact]
    public void AServiceIsReportedUnderTheNameItsCaptureRecorded()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            ServiceCycleTraceFixtures.EveryEventKind(),
            roster: RosterFor("Auto Harvest"));

        var report = Report(fixture);

        Assert.Contains("Service names come from the roster this capture recorded", report);
        // The number stays beside the name: every other view and the records themselves say 7, so a
        // reader has to be able to follow one service across the whole report.
        Assert.Contains("| Auto Harvest (7) |", report);
    }

    /// <summary>
    /// The degradation that matters most, because every capture recorded before this existed is in it.
    /// </summary>
    [Fact]
    public void ACaptureWithNoRosterStillReportsUnderNumericIdentities()
    {
        using var fixture = new ManualFullTraceTestDirectory(ServiceCycleTraceFixtures.EveryEventKind());

        var report = Report(fixture);

        Assert.Contains("This capture carries no roster", report);
        Assert.Contains("| 7 |", report);
        Assert.DoesNotContain("Auto Harvest", report);
    }

    /// <summary>
    /// A roster entry the suite had no display name for still beats an ordinal: the registered
    /// identity is exact, and it shows that a name is missing rather than hiding it.
    /// </summary>
    [Fact]
    public void AnEntryWithNoDisplayNameFallsBackToTheRegisteredIdentity()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            ServiceCycleTraceFixtures.EveryEventKind(),
            roster: RosterFor(string.Empty));

        var report = Report(fixture);

        Assert.Contains("| orbautomata.auto-harvest (7) |", report);
    }

    [Fact]
    public void AReportCannotBeWrittenOverTheRosterItWouldRead()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            ServiceCycleTraceFixtures.EveryEventKind(),
            roster: RosterFor("Auto Harvest"));
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);

        Assert.Throws<InvalidOperationException>(() =>
            session.EnsureSafeReportPath(Path.Combine(fixture.SessionPath, TraceRosterFormat.FileName)));
    }

    private static string Report(ManualFullTraceTestDirectory fixture)
    {
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var output = new StringWriter();
        ManualFullTraceReport.Write(output, session);
        return output.ToString();
    }
}
