using System;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplaySemanticJoinTests
{
    [Fact]
    public void SemanticJoinUsesBoundedIndexedWorkForManyCaptureFooters()
    {
        const int cycleCount = 2_000;
        var session = new ServiceCycleTraceSessionId(918);
        var service = new ServiceCycleTraceServiceId(1);
        var events = new ServiceCycleSemanticEvent[cycleCount * 5];
        var footers = new ServiceCycleReplayArtifactFooter[cycleCount];
        for (var index = 0; index < cycleCount; index++)
        {
            var generation = checked((ulong)index + 1);
            var offset = index * 5;
            var attempt = checked((ulong)offset + 1);
            var ready = attempt + 1;
            var captureStarted = ready + 1;
            var captureCompleted = captureStarted + 1;
            var queued = captureCompleted + 1;
            var cycle = new ServiceCycleReplayCycleKey(1, 1, generation, 1, generation, generation);
            var traceCycle = new ServiceCycleTraceCycleIdentity(service, 1, generation, 1, generation, generation);
            var capture = new ServiceCycleTraceCaptureIdentity(service, 1, generation, generation, generation);
            events[offset] = Event(session, attempt, ServiceCycleSemanticEventKind.StartAttempted,
                ServiceCycleSemanticPayload.StartAttempted(service, 1, generation, offset));
            events[offset + 1] = Event(session, ready, ServiceCycleSemanticEventKind.StartReady,
                ServiceCycleSemanticPayload.StartReady(
                    service, 1, generation, CommonServiceDecisionCodes.Ready.Value, offset + 1, 1), attempt);
            events[offset + 2] = Event(session, captureStarted, ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, offset + 2, 0), ready);
            events[offset + 3] = Event(session, captureCompleted, ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in capture, 1, CommonServiceDecisionCodes.Captured.Value, offset + 3, 1), captureStarted);
            events[offset + 4] = Event(session, queued, ServiceCycleSemanticEventKind.CycleQueued,
                ServiceCycleSemanticPayload.CycleFact(
                    in traceCycle, CommonServiceDecisionCodes.Ready.Value, offset + 4, 1), captureCompleted);
            footers[index] = AbortedFooter(
                cycle, ServiceCycleReplayCycleFooterDisposition.EvaluationAborted, retained: 0);
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, events, bytes);
        var trace = ServiceCycleTraceCodec.Decode(bytes);
        var work = new ServiceCycleReplayFormatWorkCounter();

        var joined = ServiceCycleReplaySemanticJoiner.Join(
            trace, Recording(session, records: 0), footers, Array.Empty<ServiceCycleReplayArtifactRecord>(), work);

        Assert.Equal(cycleCount, joined.Footers.Length);
        Assert.Equal(0UL, joined.FirstMissingFooterSemanticSequence);
        Assert.True(
            work.Operations <= events.Length * 10L + cycleCount * 2L,
            $"Indexed join used {work.Operations} operations for {events.Length} events and {cycleCount} footers.");
    }

    [Theory]
    [InlineData(ServiceCycleSemanticEventKind.CaptureStarted, ServiceCycleReplaySemanticJoinCode.CaptureStartedMissing)]
    [InlineData(ServiceCycleSemanticEventKind.CaptureCompleted, ServiceCycleReplaySemanticJoinCode.CaptureCompletedMissing)]
    [InlineData(ServiceCycleSemanticEventKind.CycleQueued, ServiceCycleReplaySemanticJoinCode.CycleQueuedMissing)]
    [InlineData(ServiceCycleSemanticEventKind.CycleStarted, ServiceCycleReplaySemanticJoinCode.CycleStartedMissing)]
    [InlineData(ServiceCycleSemanticEventKind.EvaluationStarted, ServiceCycleReplaySemanticJoinCode.EvaluationStartedMissing)]
    public void RequiredCaptureQueueAndStartEvidenceCannotBeOmitted(
        ServiceCycleSemanticEventKind omitted,
        ServiceCycleReplaySemanticJoinCode expected)
    {
        var trace = SemanticWithPriorEmergency(out var current, out var prior);
        var indices = CurrentCycleIndices(trace, current)
            .Where(index => trace[index].Kind != omitted)
            .ToArray();

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace,
            Footer(current, prior, emergencyTransition: 3),
            Records(current),
            indices);

        Assert.Equal(expected, join.Code);
    }

    [Fact]
    public void RequiredChainRejectsReparentedEvidence()
    {
        var trace = SemanticWithPriorEmergency(
            out var current, out var prior, reparentEvaluationStart: true);

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace,
            Footer(current, prior, emergencyTransition: 3),
            Records(current),
            CurrentCycleIndices(trace, current));

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.CausalParentMismatch, join.Code);
    }

    [Fact]
    public void ProvisionalFooterWakeMustEqualEvaluationEvidence()
    {
        var trace = SemanticWithPriorEmergency(out var current, out var prior);
        var footer = Footer(
            current,
            prior,
            emergencyTransition: 3,
            returnedWake: WakePolicy.AfterBatch(new MonotonicDuration(1)));

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace, footer, Records(current), CurrentCycleIndices(trace, current));

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.WakeMismatch, join.Code);
    }

    [Theory]
    [InlineData(ServiceCycleReplayCycleFooterDisposition.EvaluationAborted, false)]
    [InlineData(ServiceCycleReplayCycleFooterDisposition.ProjectionAborted, true)]
    public void AbortedFootersJoinOnlyTheirExactPhaseEvidence(
        ServiceCycleReplayCycleFooterDisposition disposition,
        bool hasNextState)
    {
        var trace = AbortedSemantic(includeForbiddenState: false, disposition, out var cycle);
        var records = AbortedRecords(cycle, hasNextState);
        var footer = AbortedFooter(cycle, disposition, records.Length);

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace, footer, records, Enumerable.Range(0, trace.Count).ToArray());

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.Complete, join.Code);
        Assert.Equal(
            hasNextState
                ? ServiceCycleSemanticEventKind.EvaluationCompleted
                : ServiceCycleSemanticEventKind.EvaluationFaulted,
            join.EvaluationTerminalKind);
        Assert.Equal(ServiceCycleSemanticEventKind.CycleFaulted, join.CycleTerminalKind);
    }

    [Fact]
    public void AbortedFooterCannotClaimStateOrBatchPublication()
    {
        var trace = AbortedSemantic(
            includeForbiddenState: true,
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
            out var cycle);
        var records = AbortedRecords(cycle, hasNextState: false);

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace,
            AbortedFooter(cycle, ServiceCycleReplayCycleFooterDisposition.EvaluationAborted, records.Length),
            records,
            Enumerable.Range(0, trace.Count).ToArray());

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid, join.Code);
    }

    [Fact]
    public void ProjectionAbortCannotReuseEvaluationFaultEvidence()
    {
        var trace = AbortedSemantic(
            includeForbiddenState: false,
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
            out var cycle);
        var records = AbortedRecords(cycle, hasNextState: true);

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace,
            AbortedFooter(cycle, ServiceCycleReplayCycleFooterDisposition.ProjectionAborted, records.Length),
            records,
            Enumerable.Range(0, trace.Count).ToArray());

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.AbortedFooterEvidenceInvalid, join.Code);
    }

    [Fact]
    public void ProjectionAbortWakeMustEqualProjectionFaultEvidence()
    {
        var trace = AbortedSemantic(
            includeForbiddenState: false,
            ServiceCycleReplayCycleFooterDisposition.ProjectionAborted,
            out var cycle);
        var records = AbortedRecords(cycle, hasNextState: true);

        var join = ServiceCycleReplayCycleJoiner.Join(
            trace,
            AbortedFooter(
                cycle,
                ServiceCycleReplayCycleFooterDisposition.ProjectionAborted,
                records.Length,
                WakePolicy.AfterBatch(new MonotonicDuration(1))),
            records,
            Enumerable.Range(0, trace.Count).ToArray());

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.WakeMismatch, join.Code);
    }

    [Fact]
    public void ExactlyJoinedEvaluationAbortRoundTripsAsExecutionIneligible()
    {
        var semantic = AbortedSemantic(
            includeForbiddenState: false,
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
            out var key);
        var session = new ServiceCycleReplaySession(
            semantic.Session,
            new ServiceCycleReplaySessionOptions(true, 16, 8, 2));
        var descriptor = new ServiceCycleReplayCodecDescriptor(1, 1);
        session.BindCodecManifest(1, new object(), descriptor, descriptor, descriptor);
        var payload = new byte[] { 1 };
        Assert.True(session.TryAppendRecord(
            in key,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.CycleInput, 0),
            in descriptor,
            payload,
            1,
            out _));
        Assert.True(session.TryAppendRecord(
            in key,
            new ServiceCycleReplayRecordIdentity(ServiceCycleReplayRecordKind.PreviousState, 0),
            in descriptor,
            payload,
            1,
            out _));
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.replay-abort"),
            new LifecycleGeneration(2),
            new ConfigGeneration(3),
            new StrategyGeneration(4),
            new CaptureSequence(5),
            new CycleId(6));
        var context = new ServiceCycleReplayContext(
            1,
            new ServiceCycleContext(identity, default, new MonotonicTimestamp(5)));
        var footer = new ServiceCycleReplayCycleFooter(
            0,
            context,
            ServiceCycleReplayCycleFooterDisposition.EvaluationAborted,
            default,
            false,
            default,
            false,
            0,
            1,
            2,
            2,
            ServiceCycleReplayCompleteness.Complete,
            1,
            10_000_000,
            0);
        Assert.True(session.TryAppendFooter(in footer, out _));
        Assert.True(session.TryReadSnapshot(out var snapshot));
        var events = Enumerable.Range(0, semantic.Count).Select(index => semantic[index]).ToArray();
        var semanticBytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(semantic.Session, semantic.Dropped, events, semanticBytes);

        var encoded = ServiceCycleReplayArtifactCodec.Encode(semanticBytes, session, in snapshot);
        var decoded = ServiceCycleReplayArtifactCodec.Decode(encoded);

        Assert.False(decoded.IsComplete);
        Assert.Equal(ServiceCycleReplayArtifactEligibilityCode.EvaluationAborted, decoded.Eligibility);
        Assert.Equal(ServiceCycleReplayCompletenessCode.CycleIncomplete, decoded.Completeness.Code);
        Assert.Equal(ServiceCycleReplaySemanticJoinCode.Complete, decoded.GetCycle(0).Join.Code);
        Assert.False(decoded.GetCycle(0).IsComplete);
    }

    [Fact]
    public void SemanticActionCountIsBoundedByAvailableEventsBeforeAllocation()
    {
        var session = new ServiceCycleTraceSessionId(912);
        var cycle = new ServiceCycleTraceCycleIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var terminal = Event(
            session,
            1,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticPayload.BatchFact(
                in cycle,
                1,
                (int)BatchTerminalDisposition.Completed,
                CommonActionResultCodes.Committed.Value,
                int.MaxValue,
                int.MaxValue,
                -1,
                0,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                1));
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(1)];
        ServiceCycleTraceCodec.Encode(session, default, new[] { terminal }, bytes);
        var trace = ServiceCycleTraceCodec.Decode(bytes);

        var code = ServiceCycleReplaySemanticActionValidator.Validate(
            trace, new[] { 0 }, default, trace[0]);

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.ActionEvidenceMissing, code);
    }

    [Theory]
    [InlineData(false, true, true, false, false, ServiceCycleReplaySemanticJoinCode.ActionAttemptMissing)]
    [InlineData(true, false, true, false, false, ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch)]
    [InlineData(true, true, false, false, false, ServiceCycleReplaySemanticJoinCode.BatchTerminalCausalityMismatch)]
    [InlineData(true, true, true, true, false, ServiceCycleReplaySemanticJoinCode.ActionAttemptOrderMismatch)]
    [InlineData(true, true, true, false, true, ServiceCycleReplaySemanticJoinCode.ActionAttemptDuplicate)]
    public void ActionAndBatchCausalEvidenceIsExact(
        bool includeAttempt,
        bool terminalParentsAttempt,
        bool publicationAncestor,
        bool outOfOrderAttempt,
        bool duplicateAttempt,
        ServiceCycleReplaySemanticJoinCode expected)
    {
        var trace = ActionTrace(
            includeAttempt,
            terminalParentsAttempt,
            publicationAncestor,
            outOfOrderAttempt,
            duplicateAttempt,
            out var published,
            out var terminal);

        var code = ServiceCycleReplaySemanticActionValidator.Validate(
            trace,
            Enumerable.Range(0, trace.Count).ToArray(),
            published,
            terminal);

        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(false, false, ServiceCycleReplaySemanticJoinCode.Complete)]
    [InlineData(true, false, ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch)]
    [InlineData(false, true, ServiceCycleReplaySemanticJoinCode.BatchTerminalCausalityMismatch)]
    public void MultiActionChainRequiresEveryInterStepAncestor(
        bool detachSecondAttempt,
        bool detachBatchTerminal,
        ServiceCycleReplaySemanticJoinCode expected)
    {
        var trace = TwoActionTrace(
            detachSecondAttempt,
            detachBatchTerminal,
            out var published,
            out var terminal);

        var code = ServiceCycleReplaySemanticActionValidator.Validate(
            trace,
            Enumerable.Range(0, trace.Count).ToArray(),
            published,
            terminal);

        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(false, ServiceCycleReplaySemanticJoinCode.Complete)]
    [InlineData(true, ServiceCycleReplaySemanticJoinCode.ActionAttemptCausalityMismatch)]
    public void EmergencySharedParentStillRequiresAnExactCommittedPrefix(
        bool detachCommittedPrefix,
        ServiceCycleReplaySemanticJoinCode expected)
    {
        var trace = EmergencyActionTrace(
            detachCommittedPrefix,
            out var published,
            out var terminal);

        var code = ServiceCycleReplaySemanticActionValidator.Validate(
            trace,
            Enumerable.Range(0, trace.Count).ToArray(),
            published,
            terminal);

        Assert.Equal(expected, code);
    }

    [Fact]
    public void PreviousEmergencyReceiptDerivesTransitionAcrossEnterClearReenter()
    {
        var trace = SemanticWithPriorEmergency(out var current, out var prior);
        var records = Records(current);
        var recording = Recording(trace.Session, records.Length);
        var exact = ServiceCycleReplaySemanticJoiner.Join(
            trace, recording, new[] { Footer(current, prior, emergencyTransition: 3) }, records);
        var mismatch = ServiceCycleReplaySemanticJoiner.Join(
            trace, recording, new[] { Footer(current, prior, emergencyTransition: 2) }, records);

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.Complete, exact.Footers[0].Join.Code);
        Assert.Equal(
            ServiceCycleReplaySemanticJoinCode.PreviousReceiptMismatch,
            mismatch.Footers[0].Join.Code);
    }

    [Theory]
    [InlineData(false, false, true, false, ServiceCycleReplaySemanticJoinCode.ConfigurationPublicationMissing)]
    [InlineData(true, false, false, false, ServiceCycleReplaySemanticJoinCode.ConfigurationPublicationDuplicate)]
    [InlineData(false, false, false, true, ServiceCycleReplaySemanticJoinCode.StrategyPublicationMissing)]
    [InlineData(false, true, false, false, ServiceCycleReplaySemanticJoinCode.StrategyPublicationDuplicate)]
    public void ReferencedPublicationEvidenceMustBePresentExactlyOnce(
        bool duplicateConfiguration,
        bool duplicateStrategy,
        bool omitConfiguration,
        bool omitStrategy,
        ServiceCycleReplaySemanticJoinCode expected)
    {
        var trace = PublicationTrace(
            duplicateConfiguration,
            duplicateStrategy,
            omitConfiguration,
            omitStrategy,
            out var cycle);
        var captureStarted = Enumerable.Range(0, trace.Count)
            .Select(index => trace[index])
            .Single(item => item.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        var captureCompleted = Enumerable.Range(0, trace.Count)
            .Select(index => trace[index])
            .Single(item => item.Kind == ServiceCycleSemanticEventKind.CaptureCompleted);
        var footer = Footer(cycle, default, emergencyTransition: 0);

        var code = ServiceCycleReplayPublicationEvidenceValidator.Validate(
            trace, in footer, captureStarted, captureCompleted);

        Assert.Equal(expected, code);
    }

    [Fact]
    public void ReferencedPublicationEvidenceMustPrecedeItsCapturePhase()
    {
        var trace = PublicationTrace(
            false, false, false, false, out var cycle, publicationsAfterCapture: true);
        var captureStarted = Enumerable.Range(0, trace.Count)
            .Select(index => trace[index])
            .Single(item => item.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        var captureCompleted = Enumerable.Range(0, trace.Count)
            .Select(index => trace[index])
            .Single(item => item.Kind == ServiceCycleSemanticEventKind.CaptureCompleted);
        var footer = Footer(cycle, default, emergencyTransition: 0);

        var code = ServiceCycleReplayPublicationEvidenceValidator.Validate(
            trace, in footer, captureStarted, captureCompleted);

        Assert.Equal(ServiceCycleReplaySemanticJoinCode.PublicationOrderMismatch, code);
    }

    private static ServiceCycleTraceDocument SemanticWithPriorEmergency(
        out ServiceCycleReplayCycleKey current,
        out ServiceCycleReplayCycleKey prior,
        bool reparentEvaluationStart = false)
    {
        var session = new ServiceCycleTraceSessionId(911);
        current = new ServiceCycleReplayCycleKey(1, 2, 3, 4, 5, 6);
        prior = new ServiceCycleReplayCycleKey(1, 2, 3, 4, 4, 5);
        var currentTrace = new ServiceCycleTraceCycleIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var currentCapture = new ServiceCycleTraceCaptureIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 5, 6);
        var priorTrace = new ServiceCycleTraceCycleIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 4, 4, 5);
        var projection = default(ServiceStateProjectionSnapshot);
        var fingerprint = ServiceCycleProjectionFingerprint.Compute(in projection);
        var events = new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticPayload.Publication(false, currentTrace.Service, 3, 1)),
            Event(session, 2, ServiceCycleSemanticEventKind.StrategyPublished,
                ServiceCycleSemanticPayload.Publication(true, currentTrace.Service, 4, 2), 1),
            Event(session, 3, ServiceCycleSemanticEventKind.EmergencyEntered,
                ServiceCycleSemanticPayload.Emergency((int)EmergencyStopReason.UserRequested, 1, 10)),
            Event(session, 4, ServiceCycleSemanticEventKind.EmergencyCleared,
                ServiceCycleSemanticPayload.Emergency((int)EmergencyStopReason.UserRequested, 1, 20)),
            Event(session, 5, ServiceCycleSemanticEventKind.EmergencyEntered,
                ServiceCycleSemanticPayload.Emergency((int)EmergencyStopReason.SafetyInterlock, 2, 30)),
            Event(session, 6, ServiceCycleSemanticEventKind.ActionRejected,
                ServiceCycleSemanticPayload.ActionFact(in priorTrace, 7, 1, 0,
                    (int)ServiceActionDisposition.Rejected, CommonActionResultCodes.EmergencyStop.Value,
                    null, 0, 0, 0, 50, 0), parent: 5),
            Event(session, 7, ServiceCycleSemanticEventKind.BatchAborted,
                ServiceCycleSemanticPayload.BatchFact(in priorTrace, 7, (int)BatchTerminalDisposition.Rejected,
                    CommonActionResultCodes.EmergencyStop.Value, 1, 0, 0, 0, 0, 0, 0, 50), parent: 5),
            Event(session, 8, ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in currentCapture, 0, 0, 55, 0), parent: 7),
            Event(session, 9, ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in currentCapture, 4, CommonServiceDecisionCodes.Captured.Value, 56, 1), parent: 8),
            Event(session, 10, ServiceCycleSemanticEventKind.CycleQueued,
                ServiceCycleSemanticPayload.CycleFact(
                    in currentTrace, CommonServiceDecisionCodes.Ready.Value, 57, 1), parent: 9),
            Event(session, 11, ServiceCycleSemanticEventKind.CycleStarted,
                ServiceCycleSemanticPayload.CycleFact(in currentTrace, 0, 60, 0), parent: 10),
            Event(session, 12, ServiceCycleSemanticEventKind.EvaluationStarted,
                ServiceCycleSemanticPayload.Evaluation(in currentTrace, 0, 0, 60, 0),
                parent: reparentEvaluationStart ? 9UL : 11UL),
            Event(session, 13, ServiceCycleSemanticEventKind.StatePublished,
                ServiceCycleSemanticPayload.State(in currentTrace, 1, fingerprint, 61), parent: 12),
            Event(session, 14, ServiceCycleSemanticEventKind.EvaluationCompleted,
                ServiceCycleSemanticPayload.EvaluationCompleted(
                    in currentTrace, 0, WakePolicy.Immediate, 62, 2), parent: 13),
            Event(session, 15, ServiceCycleSemanticEventKind.BatchPublished,
                ServiceCycleSemanticPayload.BatchFact(
                    in currentTrace, 8, 0, 0, 0, 0, -1, 0, 0, 0, 0, 63), parent: 14),
            Event(session, 16, ServiceCycleSemanticEventKind.BatchCompleted,
                ServiceCycleSemanticPayload.BatchFact(in currentTrace, 8, (int)BatchTerminalDisposition.Completed,
                    CommonActionResultCodes.Committed.Value, 0, 0, -1, 0, 0, 0, 0, 64), parent: 15),
            Event(session, 17, ServiceCycleSemanticEventKind.CycleCompleted,
                ServiceCycleSemanticPayload.CycleFact(in currentTrace, 0, 64, 0), parent: 16),
        };
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, events, bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private static ServiceCycleTraceDocument PublicationTrace(
        bool duplicateConfiguration,
        bool duplicateStrategy,
        bool omitConfiguration,
        bool omitStrategy,
        out ServiceCycleReplayCycleKey cycle,
        bool publicationsAfterCapture = false)
    {
        var session = new ServiceCycleTraceSessionId(914);
        cycle = new ServiceCycleReplayCycleKey(1, 2, 3, 4, 5, 6);
        var traceCycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var capture = new ServiceCycleTraceCaptureIdentity(
            new ServiceCycleTraceServiceId(1), 2, 3, 5, 6);
        var events = new System.Collections.Generic.List<ServiceCycleSemanticEvent>();
        void Add(ServiceCycleSemanticEventKind kind, ServiceCycleSemanticPayload payload)
        {
            var sequence = checked((ulong)events.Count + 1);
            events.Add(Event(session, sequence, kind, payload, sequence - 1));
        }
        void AddPublications()
        {
            if (!omitConfiguration)
            {
                Add(ServiceCycleSemanticEventKind.ConfigurationPublished,
                    ServiceCycleSemanticPayload.Publication(
                        false, traceCycle.Service, 3, publicationsAfterCapture ? 7 : 1));
                if (duplicateConfiguration)
                    Add(ServiceCycleSemanticEventKind.ConfigurationPublished,
                        ServiceCycleSemanticPayload.Publication(false, traceCycle.Service, 3, 2));
            }
            if (!omitStrategy)
            {
                Add(ServiceCycleSemanticEventKind.StrategyPublished,
                    ServiceCycleSemanticPayload.Publication(
                        true, traceCycle.Service, 4, publicationsAfterCapture ? 8 : 3));
                if (duplicateStrategy)
                    Add(ServiceCycleSemanticEventKind.StrategyPublished,
                        ServiceCycleSemanticPayload.Publication(true, traceCycle.Service, 4, 4));
            }
        }
        void AddCaptures()
        {
            Add(ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 5, 0));
            Add(ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in capture, 4, CommonServiceDecisionCodes.Captured.Value, 6, 1));
        }
        if (publicationsAfterCapture)
        {
            AddCaptures();
            AddPublications();
        }
        else
        {
            AddPublications();
            AddCaptures();
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Count)];
        ServiceCycleTraceCodec.Encode(session, default, events.ToArray(), bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private static ServiceCycleTraceDocument ActionTrace(
        bool includeAttempt,
        bool terminalParentsAttempt,
        bool publicationAncestor,
        bool outOfOrderAttempt,
        bool duplicateAttempt,
        out ServiceCycleSemanticEvent published,
        out ServiceCycleSemanticEvent terminal)
    {
        var session = new ServiceCycleTraceSessionId(915);
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var actionCount = outOfOrderAttempt ? 2 : 1;
        var events = new System.Collections.Generic.List<ServiceCycleSemanticEvent>
        {
            Event(session, 1, ServiceCycleSemanticEventKind.BatchPublished,
                ServiceCycleSemanticPayload.BatchFact(
                    in cycle, 8, 0, 0, actionCount, 0, -1, 0, 0, 0, 0, 1)),
        };
        ulong attemptSequence = 0;
        if (includeAttempt)
        {
            attemptSequence = checked((ulong)events.Count + 1);
            events.Add(Event(
                session,
                attemptSequence,
                ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, outOfOrderAttempt ? 1 : 0, 0, 0, null, 0, 0, 0, 2, 0),
                publicationAncestor ? 1UL : 0));
            if (duplicateAttempt)
            {
                var duplicateSequence = checked((ulong)events.Count + 1);
                events.Add(Event(
                    session,
                    duplicateSequence,
                    ServiceCycleSemanticEventKind.ActionAttempted,
                    ServiceCycleSemanticPayload.ActionFact(
                        in cycle, 8, 10, 0, 0, 0, null, 0, 0, 0, 2, 0),
                    attemptSequence));
            }
        }
        if (!outOfOrderAttempt)
        {
            var committedSequence = checked((ulong)events.Count + 1);
            events.Add(Event(
                session,
                committedSequence,
                ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, 0, (int)ServiceActionDisposition.Committed,
                    CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified,
                    1, 1, 1, 3, 1),
                terminalParentsAttempt && attemptSequence != 0 ? attemptSequence : 1));
        }
        var terminalSequence = checked((ulong)events.Count + 1);
        events.Add(Event(
            session,
            terminalSequence,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticPayload.BatchFact(
                in cycle, 8, (int)BatchTerminalDisposition.Completed,
                CommonActionResultCodes.Committed.Value, actionCount, actionCount, -1, 0,
                outOfOrderAttempt ? 2 : 1,
                outOfOrderAttempt ? 2 : 1,
                outOfOrderAttempt ? 2 : 1,
                4),
            terminalSequence - 1));
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Count)];
        ServiceCycleTraceCodec.Encode(session, default, events.ToArray(), bytes);
        var trace = ServiceCycleTraceCodec.Decode(bytes);
        published = trace[0];
        terminal = trace[trace.Count - 1];
        return trace;
    }

    private static ServiceCycleTraceDocument TwoActionTrace(
        bool detachSecondAttempt,
        bool detachBatchTerminal,
        out ServiceCycleSemanticEvent published,
        out ServiceCycleSemanticEvent terminal)
    {
        var session = new ServiceCycleTraceSessionId(916);
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var events = new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.BatchPublished,
                ServiceCycleSemanticPayload.BatchFact(
                    in cycle, 8, 0, 0, 2, 0, -1, 0, 0, 0, 0, 1)),
            Event(session, 2, ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, 0, 0, 0, null, 0, 0, 0, 2, 0), 1),
            Event(session, 3, ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, 0, (int)ServiceActionDisposition.Committed,
                    CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified,
                    1, 1, 1, 3, 1), 2),
            Event(session, 4, ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticPayload.Publication(false, cycle.Service, 9, 4), 3),
            Event(session, 5, ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 11, 1, 0, 0, null, 0, 0, 0, 5, 0),
                detachSecondAttempt ? 1UL : 4UL),
            Event(session, 6, ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 11, 1, (int)ServiceActionDisposition.Committed,
                    CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified,
                    1, 1, 1, 6, 1), 5),
            Event(session, 7, ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticPayload.Publication(false, cycle.Service, 10, 7), 6),
            Event(session, 8, ServiceCycleSemanticEventKind.BatchCompleted,
                ServiceCycleSemanticPayload.BatchFact(
                    in cycle, 8, (int)BatchTerminalDisposition.Completed,
                    CommonActionResultCodes.Committed.Value, 2, 2, -1, 0, 2, 2, 2, 8),
                detachBatchTerminal ? 1UL : 7UL),
        };
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, events, bytes);
        var trace = ServiceCycleTraceCodec.Decode(bytes);
        published = trace[0];
        terminal = trace[^1];
        return trace;
    }

    private static ServiceCycleTraceDocument EmergencyActionTrace(
        bool detachCommittedPrefix,
        out ServiceCycleSemanticEvent published,
        out ServiceCycleSemanticEvent terminal)
    {
        var session = new ServiceCycleTraceSessionId(917);
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var events = new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.BatchPublished,
                ServiceCycleSemanticPayload.BatchFact(
                    in cycle, 8, 0, 0, 3, 0, -1, 0, 0, 0, 0, 1)),
            Event(session, 2, ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, 0, 0, 0, null, 0, 0, 0, 2, 0),
                detachCommittedPrefix ? 0UL : 1UL),
            Event(session, 3, ServiceCycleSemanticEventKind.ActionCommitted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 10, 0, (int)ServiceActionDisposition.Committed,
                    CommonActionResultCodes.Committed.Value, NativeMutationOutcome.Verified,
                    1, 1, 1, 3, 1), 2),
            Event(session, 4, ServiceCycleSemanticEventKind.EmergencyEntered,
                ServiceCycleSemanticPayload.Emergency(
                    (int)EmergencyStopReason.UserRequested, 1, 4)),
            Event(session, 5, ServiceCycleSemanticEventKind.ActionRejected,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 8, 11, 1, (int)ServiceActionDisposition.Rejected,
                    CommonActionResultCodes.EmergencyStop.Value, null, 0, 0, 0, 5, 0), 4),
            Event(session, 6, ServiceCycleSemanticEventKind.BatchAborted,
                ServiceCycleSemanticPayload.BatchFact(
                    in cycle, 8, (int)BatchTerminalDisposition.Rejected,
                    CommonActionResultCodes.EmergencyStop.Value, 3, 1, 1, 1, 1, 1, 1, 6), 4),
        };
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, events, bytes);
        var trace = ServiceCycleTraceCodec.Decode(bytes);
        published = trace[0];
        terminal = trace[^1];
        return trace;
    }

    private static ServiceCycleReplayArtifactFooter Footer(
        ServiceCycleReplayCycleKey current,
        ServiceCycleReplayCycleKey prior,
        long emergencyTransition,
        WakePolicy? returnedWake = null)
    {
        var action = new ServiceCycleReplayArtifactActionResult(
            ServiceActionDisposition.Rejected,
            CommonActionResultCodes.EmergencyStop.Value,
            false,
            0,
            0,
            0,
            0);
        var receipt = new ServiceCycleReplayArtifactReceipt(
            true,
            prior,
            7,
            BatchTerminalDisposition.Rejected,
            1,
            0,
            0,
            0,
            CommonActionResultCodes.EmergencyStop.Value,
            action,
            true,
            0,
            0,
            0,
            50,
            2,
            emergencyTransition,
            (int)EmergencyStopReason.SafetyInterlock);
        return new ServiceCycleReplayArtifactFooter(
            1,
            new ServiceCycleReplayArtifactContext(current, receipt, 60),
            ServiceCycleReplayCycleFooterDisposition.Provisional,
            returnedWake ?? WakePolicy.Immediate,
            true,
            default,
            true,
            0,
            1,
            3,
            3,
            ServiceCycleReplayCompleteness.Complete,
            1,
            10_000_000,
            0,
            default);
    }

    private static ServiceCycleReplayArtifactRecord[] Records(ServiceCycleReplayCycleKey cycle)
    {
        var payload = new byte[] { 1, 2, 3 };
        return new[]
        {
            Record(1, cycle, ServiceCycleReplayRecordKind.CycleInput, payload.AsMemory(0, 1)),
            Record(2, cycle, ServiceCycleReplayRecordKind.PreviousState, payload.AsMemory(1, 1)),
            Record(3, cycle, ServiceCycleReplayRecordKind.NextState, payload.AsMemory(2, 1)),
        };
    }

    private static int[] CurrentCycleIndices(
        ServiceCycleTraceDocument trace,
        ServiceCycleReplayCycleKey key) => Enumerable.Range(0, trace.Count)
        .Where(index =>
        {
            var item = trace[index];
            if (ServiceCycleReplaySemanticMatch.Matches(item, key))
                return true;
            return item.Kind == ServiceCycleSemanticEventKind.CaptureStarted &&
                item.Payload.Service == (ulong)key.TraceServiceKey &&
                item.Payload.Lifecycle == key.Lifecycle &&
                item.Payload.Configuration == key.Configuration &&
                item.Payload.Capture == key.Capture &&
                item.Payload.Cycle == key.Cycle;
        })
        .ToArray();

    private static ServiceCycleTraceDocument AbortedSemantic(
        bool includeForbiddenState,
        ServiceCycleReplayCycleFooterDisposition disposition,
        out ServiceCycleReplayCycleKey cycle)
    {
        var session = new ServiceCycleTraceSessionId(913);
        cycle = new ServiceCycleReplayCycleKey(1, 2, 3, 4, 5, 6);
        var traceCycle = new ServiceCycleTraceCycleIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 4, 5, 6);
        var capture = new ServiceCycleTraceCaptureIdentity(new ServiceCycleTraceServiceId(1), 2, 3, 5, 6);
        var events = new System.Collections.Generic.List<ServiceCycleSemanticEvent>
        {
            Event(session, 1, ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticPayload.Publication(false, traceCycle.Service, 3, 1)),
            Event(session, 2, ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 1, 0), 1),
            Event(session, 3, ServiceCycleSemanticEventKind.StrategyPublished,
                ServiceCycleSemanticPayload.Publication(true, traceCycle.Service, 4, 2), 2),
            Event(session, 4, ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in capture, 4, CommonServiceDecisionCodes.Captured.Value, 2, 1), 2),
            Event(session, 5, ServiceCycleSemanticEventKind.CycleQueued,
                ServiceCycleSemanticPayload.CycleFact(
                    in traceCycle, CommonServiceDecisionCodes.Ready.Value, 3, 1), 4),
            Event(session, 6, ServiceCycleSemanticEventKind.CycleStarted,
                ServiceCycleSemanticPayload.CycleFact(in traceCycle, 0, 4, 0), 5),
            Event(session, 7, ServiceCycleSemanticEventKind.EvaluationStarted,
                ServiceCycleSemanticPayload.Evaluation(in traceCycle, 0, 0, 5, 0), 6),
        };
        if (disposition == ServiceCycleReplayCycleFooterDisposition.EvaluationAborted)
        {
            if (includeForbiddenState)
            {
                events.Add(Event(session, 8, ServiceCycleSemanticEventKind.StatePublished,
                    ServiceCycleSemanticPayload.State(in traceCycle, 1, 0, 6), 7));
            }
            var evaluationSequence = checked((ulong)events.Count + 1);
            events.Add(Event(session, evaluationSequence, ServiceCycleSemanticEventKind.EvaluationFaulted,
                ServiceCycleSemanticPayload.Evaluation(
                    in traceCycle, CommonActionResultCodes.AdapterFault.Value, 0, 7, 2), 7));
            events.Add(Event(session, evaluationSequence + 1, ServiceCycleSemanticEventKind.CycleFaulted,
                ServiceCycleSemanticPayload.CycleFact(
                    in traceCycle, CommonActionResultCodes.AdapterFault.Value, 7, 2), evaluationSequence));
        }
        else
        {
            events.Add(Event(session, 8, ServiceCycleSemanticEventKind.EvaluationCompleted,
                ServiceCycleSemanticPayload.EvaluationCompleted(
                    in traceCycle, 0, WakePolicy.Immediate, 6, 1), 7));
            events.Add(Event(session, 9, ServiceCycleSemanticEventKind.ProjectionFaulted,
                ServiceCycleSemanticPayload.ProjectionFaulted(
                    in traceCycle, CommonActionResultCodes.AdapterFault.Value, 0,
                    WakePolicy.Immediate, 7, 2), 8));
            if (includeForbiddenState)
            {
                events.Add(Event(session, 10, ServiceCycleSemanticEventKind.StatePublished,
                    ServiceCycleSemanticPayload.State(in traceCycle, 1, 0, 7), 9));
            }
            var cycleSequence = checked((ulong)events.Count + 1);
            events.Add(Event(session, cycleSequence, ServiceCycleSemanticEventKind.CycleFaulted,
                ServiceCycleSemanticPayload.CycleFact(
                    in traceCycle, CommonActionResultCodes.AdapterFault.Value, 7, 2), 9));
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Count)];
        ServiceCycleTraceCodec.Encode(session, default, events.ToArray(), bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private static ServiceCycleReplayArtifactFooter AbortedFooter(
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayCycleFooterDisposition disposition,
        int retained,
        WakePolicy? returnedWake = null)
    {
        var hasWake = disposition == ServiceCycleReplayCycleFooterDisposition.ProjectionAborted;
        return new ServiceCycleReplayArtifactFooter(
            1,
            new ServiceCycleReplayArtifactContext(cycle, default, 5),
            disposition,
            hasWake ? returnedWake ?? WakePolicy.Immediate : default,
            hasWake,
            default,
            false,
            0,
            1,
            retained,
            retained,
            ServiceCycleReplayCompleteness.Complete,
            1,
            10_000_000,
            0,
            default);
    }

    private static ServiceCycleReplayArtifactRecord[] AbortedRecords(
        ServiceCycleReplayCycleKey cycle,
        bool hasNextState)
    {
        var payload = hasNextState ? new byte[] { 1, 2, 3 } : new byte[] { 1, 2 };
        var records = new ServiceCycleReplayArtifactRecord[payload.Length];
        records[0] = Record(1, cycle, ServiceCycleReplayRecordKind.CycleInput, payload.AsMemory(0, 1));
        records[1] = Record(2, cycle, ServiceCycleReplayRecordKind.PreviousState, payload.AsMemory(1, 1));
        if (hasNextState)
            records[2] = Record(3, cycle, ServiceCycleReplayRecordKind.NextState, payload.AsMemory(2, 1));
        return records;
    }

    private static ServiceCycleReplayArtifactRecord Record(
        long sequence,
        ServiceCycleReplayCycleKey cycle,
        ServiceCycleReplayRecordKind kind,
        ReadOnlyMemory<byte> payload) => new(
            sequence,
            cycle,
            new ServiceCycleReplayRecordIdentity(kind, 0),
            1,
            payload,
            ServiceCycleReplayCrc32.Compute(payload.Span));

    private static ServiceCycleReplayRecordingSnapshot Recording(ServiceCycleTraceSessionId session, int records) => new(
        session,
        true,
        new ServiceCycleReplayCodecManifestFence(1, 1),
        new ServiceCycleReplayHighWaterFence(4, records, 1, records, 1, records),
        default,
        ServiceCycleReplayCompleteness.Complete,
        default);

    private static ServiceCycleSemanticEvent Event(
        ServiceCycleTraceSessionId session,
        ulong sequence,
        ServiceCycleSemanticEventKind kind,
        ServiceCycleSemanticPayload payload,
        ulong parent = 0) => new(
            new ServiceCycleTraceEventId(session, sequence),
            parent == 0 ? default : new ServiceCycleTraceEventId(session, parent),
            kind,
            in payload);
}
