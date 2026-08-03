using System;
using System.IO;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
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
    public void AttributedActionSurfacesItsExactWireIdentityAndOutcome()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "orb-trace-dashboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = Path.Combine(root, "run-20260101-000000-test");
            var fullSession = Path.Combine(run, "full", "session-000000000000002a");
            Directory.CreateDirectory(fullSession);
            WriteFullTrace(fullSession);

            var candidate = new Guid("11111111-1111-1111-1111-111111111111");
            var list = new Guid("22222222-2222-2222-2222-222222222222");
            var view = new Guid("33333333-3333-3333-3333-333333333333");
            WriteJournal(run, Action(candidate, list, view));

            var document = TraceDashboardReader.Read(TraceCaptureLocator.Locate(run));
            var decision = Assert.Single(document.Decisions);

            Assert.Equal(DecisionJournalRecordKind.Action.ToString(), decision.Kind);
            Assert.Equal(1, decision.ActionOrdinal);
            Assert.Equal(candidate.ToString("D"), decision.CandidateId);
            Assert.Equal(ServiceActionNativeTypeId.UpgradeSO.ToString(), decision.NativeType);
            Assert.Equal(list.ToString("D"), decision.ListId);
            Assert.Equal(view.ToString("D"), decision.ViewId);
            Assert.Equal(ServiceActionRouteStatus.Resolved.ToString(), decision.RouteStatus);
            Assert.Equal("Committed", decision.Outcome);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFullTrace(string session)
    {
        var fullSession = new FullTraceSessionId(42);
        var semanticSession = new ServiceCycleTraceSessionId(101);
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(3),
            1,
            1,
            1,
            1,
            1);
        var payload = ServiceCycleSemanticPayload.CycleFact(in cycle, 0, 100, 10);
        var traceEvent = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(semanticSession, 1),
            default,
            ServiceCycleSemanticEventKind.CycleStarted,
            in payload);
        var segment = new byte[FullTraceSegmentCodec.GetEncodedLength(1)];
        FullTraceSegmentCodec.Encode(
            fullSession,
            semanticSession,
            0,
            1,
            3,
            new[] { traceEvent },
            segment);
        File.WriteAllBytes(Path.Combine(session, "segment-00000000.oscs"), segment);

        var manifest = new FullTraceManifestDocument(
            FullTraceCompleteness.Complete,
            FullTraceTerminalReason.UserStopped,
            fullSession,
            semanticSession,
            3,
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
                3,
                "orbautomata.auto-buy",
                "Auto Buy"),
        });
        File.WriteAllBytes(
            Path.Combine(session, TraceRosterFormat.FileName),
            TraceRosterFormat.Encode(roster));
    }

    private static DecisionJournalRecord Action(Guid candidate, Guid list, Guid view)
    {
        var cycle = new ServiceCycleIdentity(
            new ServiceId("orbautomata.auto-buy"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceActionContext(
            cycle,
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(100));
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var result = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        var fact = new ServiceActionFact(
            context,
            result,
            new MonotonicTimestamp(100),
            new MonotonicTimestamp(100));
        var attribution = ServiceActionJournalAttribution.Routed(
            candidate,
            ServiceActionNativeTypeId.UpgradeSO,
            list,
            view);
        var observation = new DecisionJournalActionObservation(
            new ServiceCycleTraceServiceId(3),
            in fact,
            in attribution);
        return DecisionJournalRecord.Action(in observation);
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
        File.WriteAllBytes(
            Path.Combine(directory, "journal-000000.osjd"),
            bytes);
    }
}
