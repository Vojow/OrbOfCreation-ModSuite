using System;
using System.IO;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.Tests.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class ManualFullTraceSessionReaderTests
{
    [Fact]
    public void CompleteSessionRendersSeparateViewsWithoutMachinePathLeakage()
    {
        using var fixture = new ManualFullTraceTestDirectory(ServiceCycleTraceFixtures.EveryEventKind());

        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var output = new StringWriter();
        ManualFullTraceReport.Write(output, session);
        var report = output.ToString();

        Assert.Equal(FullTraceSessionState.Complete, session.Document.State);
        Assert.Contains("- Eligibility: DiagnosticOnly", report);
        Assert.Contains("- Completeness: Complete", report);
        Assert.Contains("## Service view", report);
        Assert.Contains("This capture carries no roster", report);
        Assert.Contains("## Pump view", report);
        Assert.Contains("Otherwise idle accepted pumps", report);
        Assert.Contains("## Worker and service timeline", report);
        Assert.Contains("not physical thread scheduling evidence", report);
        Assert.Contains("EvaluationCompleted", report);
        Assert.DoesNotContain(Path.GetDirectoryName(fixture.SessionPath)!, report, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionFaultDoesNotDoubleWeightCompletedEvaluationTiming()
    {
        var semantic = ServiceCycleTraceFixtures.Session;
        var tenMilliseconds = TimeSpan.FromMilliseconds(10).Ticks;
        var completedPayload = ServiceCycleSemanticPayload.EvaluationCompleted(
            in ServiceCycleTraceFixtures.Cycle,
            3,
            WakePolicy.Immediate,
            100,
            tenMilliseconds);
        var projectionPayload = ServiceCycleSemanticPayload.ProjectionFaulted(
            in ServiceCycleTraceFixtures.Cycle,
            CommonActionResultCodes.AdapterFault.Value,
            3,
            WakePolicy.Immediate,
            100,
            tenMilliseconds);
        var faultPayload = ServiceCycleSemanticPayload.Evaluation(
            in ServiceCycleTraceFixtures.Cycle,
            CommonActionResultCodes.AdapterFault.Value,
            0,
            100,
            tenMilliseconds * 3);
        var events = new[]
        {
            new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(semantic, 1),
                default,
                ServiceCycleSemanticEventKind.EvaluationCompleted,
                in completedPayload),
            new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(semantic, 2),
                new ServiceCycleTraceEventId(semantic, 1),
                ServiceCycleSemanticEventKind.ProjectionFaulted,
                in projectionPayload),
            new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(semantic, 3),
                new ServiceCycleTraceEventId(semantic, 2),
                ServiceCycleSemanticEventKind.EvaluationFaulted,
                in faultPayload),
        };
        using var fixture = new ManualFullTraceTestDirectory(events);
        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var output = new StringWriter();

        ManualFullTraceServiceView.Write(output, session);

        Assert.Contains(
            "| 7 | 3 | 0 / 0 / 0 | 1 / 0 / 1 | 1 | 0 / 0 / 0 / 0 | — | 20.000 | — |",
            output.ToString());
    }

    [Fact]
    public void MissingManifestRendersTheValidatedPrefixAsInterrupted()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            new[] { ServiceCycleTraceFixtures.Event(1) },
            writeManifest: false);

        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);
        using var output = new StringWriter();
        ManualFullTraceReport.Write(output, session);
        var report = output.ToString();

        Assert.Equal(FullTraceSessionState.Interrupted, session.Document.State);
        Assert.Contains("- Completeness: Interrupted", report);
        Assert.Contains("- Terminal reason: Unavailable (manifest absent)", report);
        Assert.Contains("- Accepted records: at least 1", report);
        Assert.Contains("- First incomplete transport sequence: 2", report);
    }

    [Fact]
    public void NoncanonicalTraceShapedFileIsRejected()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            new[] { ServiceCycleTraceFixtures.Event(1) });
        File.WriteAllBytes(Path.Combine(fixture.SessionPath, "segment-01.oscs"), new byte[] { 1 });

        var error = Assert.Throws<InvalidDataException>(() =>
            ManualFullTraceSessionReader.Read(fixture.SessionPath));

        Assert.Contains("noncanonical segment name", error.Message);
    }

    [Fact]
    public void InterruptedSessionIgnoresOnlyItsExactUncommittedSegmentName()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            new[] { ServiceCycleTraceFixtures.Event(1) },
            writeManifest: false);
        var temporaryName = "segment-00000001.oscs.tmp-" + new string('a', 32);
        File.WriteAllBytes(Path.Combine(fixture.SessionPath, temporaryName), new byte[] { 1 });

        var session = ManualFullTraceSessionReader.Read(fixture.SessionPath);

        Assert.Equal(FullTraceSessionState.Interrupted, session.Document.State);
        Assert.Equal(1UL, session.Document.SegmentCount);
    }

    [Fact]
    public void ReportPathCannotReplaceSessionEvidence()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            new[] { ServiceCycleTraceFixtures.Event(1) });
        var manifestPath = Path.Combine(fixture.SessionPath, "manifest.oscm");
        var original = File.ReadAllBytes(manifestPath);
        var session = ManualFullTraceSessionReader.Read(
            fixture.SessionPath + Path.DirectorySeparatorChar);

        Assert.Throws<InvalidOperationException>(() => session.EnsureSafeReportPath(manifestPath));
        session.EnsureSafeReportPath(Path.Combine(fixture.SessionPath, "report.md"));
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void NoncanonicalManifestNameIsRejected()
    {
        using var fixture = new ManualFullTraceTestDirectory(
            new[] { ServiceCycleTraceFixtures.Event(1) },
            writeManifest: false);
        File.WriteAllBytes(Path.Combine(fixture.SessionPath, "Manifest.oscm"), new byte[] { 1 });

        var error = Assert.Throws<InvalidDataException>(() =>
            ManualFullTraceSessionReader.Read(fixture.SessionPath));

        Assert.Contains("noncanonical manifest name", error.Message);
    }
}
