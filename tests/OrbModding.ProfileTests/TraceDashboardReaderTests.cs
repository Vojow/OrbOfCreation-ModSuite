using System;
using System.Globalization;
using System.IO;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace;
using OrbModding.ServiceCycleTrace.Dashboard;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class TraceDashboardReaderTests
{
    [Fact]
    public void SpellLevelingProjectionUsesItsRecordedServiceSchema()
    {
        using var fixture = new DashboardTraceFixture(
            service: 4,
            machineId: "orbautomata.spell-level",
            Projection((10, 65), (11, 2), (12, 1)));

        var decision = fixture.ReadDecision();

        Assert.Collection(
            decision.Projection,
            entry => Assert.Equal("Captured spells", entry.Name),
            entry => Assert.Equal("Ready spells", entry.Name),
            entry => Assert.Equal("Planned actions", entry.Name));
    }

    [Fact]
    public void AutoCastProjectionUsesItsRecordedServiceSchema()
    {
        using var fixture = new DashboardTraceFixture(
            service: 5,
            machineId: "orbautomata.auto-cast",
            Projection((10, 3), (11, 3), (13, 0)));

        var decision = fixture.ReadDecision();

        Assert.Collection(
            decision.Projection,
            entry => Assert.Equal("Captured slots", entry.Name),
            entry => Assert.Equal("Eligible slots", entry.Name),
            entry => Assert.Equal("Holding charge", entry.Name));
    }

    [Fact]
    public void AutoConceptProjectionUsesItsRecordedServiceSchema()
    {
        using var fixture = new DashboardTraceFixture(
            service: 6,
            machineId: "orbautomata.auto-concept",
            Projection((10, 8), (11, 5), (15, 2)));

        var decision = fixture.ReadDecision();

        Assert.Collection(
            decision.Projection,
            entry => Assert.Equal("Captured recipes", entry.Name),
            entry => Assert.Equal("Eligible recipes", entry.Name),
            entry => Assert.Equal("Decision kind", entry.Name));
    }

    [Fact]
    public void MentorProjectionUsesItsRecordedServiceSchema()
    {
        using var fixture = new DashboardTraceFixture(
            service: 7,
            machineId: "orbmentor.mastery-sharing",
            Projection((10, 42), (11, 2), (12, 3), (13, 2)));

        var decision = fixture.ReadDecision();

        Assert.Collection(
            decision.Projection,
            entry => Assert.Equal("Last input sequence", entry.Name),
            entry => Assert.Equal("Missed inputs", entry.Name),
            entry => Assert.Equal("Planned actions", entry.Name),
            entry => Assert.Equal("Recipients", entry.Name));
    }

    [Fact]
    public void UnknownServiceProjectionFallsBackToTheRawFieldNumber()
    {
        using var fixture = new DashboardTraceFixture(
            service: 6,
            machineId: "orbautomata.unknown",
            Projection((10, 65)));

        var entry = Assert.Single(fixture.ReadDecision().Projection);

        Assert.Equal("Field 10", entry.Name);
        Assert.Equal("65", entry.Value);
    }

    private static ServiceStateProjectionSnapshot Projection(params (int Key, long Value)[] entries)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        foreach (var entry in entries)
        {
            builder.Add(
                new ServiceProjectionKey(entry.Key),
                ServiceProjectionValue.FromInteger(entry.Value));
        }
        return builder.CaptureSnapshot();
    }

    private sealed class DashboardTraceFixture : IDisposable
    {
        private static readonly FullTraceSessionId FullSession = new(42);
        private static readonly ServiceCycleTraceSessionId SemanticSession = new(101);
        private readonly string _root;
        private readonly string _run;

        internal DashboardTraceFixture(
            ulong service,
            string machineId,
            ServiceStateProjectionSnapshot projection)
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
            _run = Path.Combine(_root, "run-20260101-000000-test");
            var session = Path.Combine(
                _run,
                "full",
                "session-" + FullSession.Value.ToString("x16", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(session);
            WriteFullTrace(session, service, machineId);
            WriteJournal(_run, Decision(service, in projection));
        }

        internal TraceDashboardDecision ReadDecision()
        {
            var document = TraceDashboardReader.Read(TraceCaptureLocator.Locate(_run));
            return Assert.Single(document.Decisions);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static void WriteFullTrace(string session, ulong service, string machineId)
        {
            var cycle = new ServiceCycleTraceCycleIdentity(
                new ServiceCycleTraceServiceId(service),
                1,
                1,
                1,
                1,
                1);
            var payload = ServiceCycleSemanticPayload.CycleFact(in cycle, 0, 100, 10);
            var traceEvent = new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(SemanticSession, 1),
                default,
                ServiceCycleSemanticEventKind.CycleStarted,
                in payload);
            var segment = new byte[FullTraceSegmentCodec.GetEncodedLength(1)];
            FullTraceSegmentCodec.Encode(
                FullSession,
                SemanticSession,
                0,
                1,
                checked((int)service),
                new[] { traceEvent },
                segment);
            File.WriteAllBytes(Path.Combine(session, "segment-00000000.oscs"), segment);

            var manifest = new FullTraceManifestDocument(
                FullTraceCompleteness.Complete,
                FullTraceTerminalReason.UserStopped,
                FullSession,
                SemanticSession,
                checked((int)service),
                1,
                1,
                1,
                1,
                0,
                0,
                100,
                100,
                checked((ulong)segment.Length));
            var manifestBytes = new byte[FullTraceManifestCodec.ManifestBytes];
            FullTraceManifestCodec.Encode(in manifest, manifestBytes);
            File.WriteAllBytes(Path.Combine(session, "manifest.oscm"), manifestBytes);

            var roster = new ServiceCycleTraceRoster(new[]
            {
                new ServiceCycleTraceRosterEntry(
                    ServiceCycleTraceRoster.ServiceKind,
                    service,
                    machineId,
                    machineId),
            });
            File.WriteAllBytes(
                Path.Combine(session, TraceRosterFormat.FileName),
                TraceRosterFormat.Encode(roster));
        }

        private static DecisionJournalRecord Decision(
            ulong service,
            in ServiceStateProjectionSnapshot projection)
        {
            var cycle = new ServiceCycleIdentity(
                new ServiceId("test.service." + service.ToString(CultureInfo.InvariantCulture)),
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1));
            var terminal = BatchReceipt.Completed(
                cycle,
                new BatchId(1),
                1,
                new ServiceNativeCallTotals(1, 1, 1),
                new MonotonicTimestamp(101));
            var fault = default(ServiceFault);
            var observation = new DecisionJournalObservation(
                new ServiceCycleTraceServiceId(service),
                1,
                1,
                1,
                1,
                new MonotonicTimestamp(100),
                terminal.CompletedAt,
                CommonServiceDecisionCodes.Ready.Value,
                CommonServiceDecisionCodes.Captured.Value,
                true,
                WakePolicy.AfterBatch(new MonotonicDuration(5)),
                true,
                in projection,
                in fault,
                in terminal);
            return DecisionJournalRecord.Decision(in observation);
        }

        private static void WriteJournal(string run, DecisionJournalRecord record)
        {
            var directory = Path.Combine(run, "journal");
            Directory.CreateDirectory(directory);
            var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(1)];
            DecisionJournalSegmentCodec.Encode(
                new DecisionJournalRunId(11),
                0,
                1,
                new[] { record },
                bytes);
            File.WriteAllBytes(Path.Combine(directory, "journal-000000.osjd"), bytes);
        }
    }
}
