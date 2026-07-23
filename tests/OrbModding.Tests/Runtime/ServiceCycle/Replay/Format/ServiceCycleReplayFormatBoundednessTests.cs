using System;
using System.Collections.Generic;
using System.Diagnostics;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Format;

public sealed class ServiceCycleReplayFormatBoundednessTests
{
    [Fact]
    public void CompleteDecodeJoinAndReencodeScaleLinearlyWithNonAbortedRecords()
    {
        var smallBytes = BuildArtifact(cycleCount: 16);
        var largeBytes = BuildArtifact(cycleCount: 64);

        var smallDecode = new ServiceCycleReplayFormatWorkCounter();
        var small = ServiceCycleReplayArtifactCodec.Decode(
            smallBytes, ServiceCycleReplayArtifactLimits.Default, smallDecode);
        var largeDecode = new ServiceCycleReplayFormatWorkCounter();
        var large = ServiceCycleReplayArtifactCodec.Decode(
            largeBytes, ServiceCycleReplayArtifactLimits.Default, largeDecode);

        var smallEncode = new ServiceCycleReplayFormatWorkCounter();
        ServiceCycleReplayArtifactCodec.Reencode(small, new byte[small.EncodedLength], smallEncode);
        var largeEncode = new ServiceCycleReplayFormatWorkCounter();
        ServiceCycleReplayArtifactCodec.Reencode(large, new byte[large.EncodedLength], largeEncode);

        Assert.True(small.IsComplete);
        Assert.True(large.IsComplete);
        Assert.Equal(16 * 3, SumRecords(small));
        Assert.Equal(64 * 3, SumRecords(large));
        Assert.InRange(largeDecode.Operations, smallDecode.Operations, smallDecode.Operations * 5);
        Assert.InRange(largeEncode.Operations, smallEncode.Operations, smallEncode.Operations * 5);
    }

    private static int SumRecords(ServiceCycleReplayArtifactDocument artifact)
    {
        var count = 0;
        for (var index = 0; index < artifact.CycleCount; index++)
            count += artifact.GetCycle(index).RecordCount;
        return count;
    }

    private static byte[] BuildArtifact(int cycleCount)
    {
        var traceSession = new ServiceCycleTraceSessionId(checked((ulong)950 + (ulong)cycleCount));
        var session = new ServiceCycleReplaySession(
            traceSession,
            new ServiceCycleReplaySessionOptions(
                true,
                byteCapacity: cycleCount * 3,
                recordCapacity: cycleCount * 3,
                cycleFooterCapacity: cycleCount));
        var descriptor = new ServiceCycleReplayCodecDescriptor(1, 8);
        session.BindCodecManifest(1, new object(), descriptor, descriptor, descriptor);
        var events = new List<ServiceCycleSemanticEvent>(cycleCount * 14);
        var scratch = new byte[8];
        var service = new ServiceId("test.replay-format-bounded");
        var traceService = new ServiceCycleTraceServiceId(1);
        var projection = default(ServiceStateProjectionSnapshot);
        var fingerprint = ServiceCycleProjectionFingerprint.Compute(in projection);
        ulong sequence = 0;
        for (var index = 0; index < cycleCount; index++)
        {
            var generation = checked((ulong)index + 1);
            var identity = new ServiceCycleIdentity(
                service,
                new LifecycleGeneration(2),
                new ConfigGeneration(generation),
                new StrategyGeneration(generation),
                new CaptureSequence(generation),
                new CycleId(generation));
            var key = new ServiceCycleReplayCycleKey(1, in identity);
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.CycleInput, 0, 11);
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.PreviousState, 0, 22);
            Append(session, in key, descriptor, scratch, ServiceCycleReplayRecordKind.NextState, 0, 33);
            var traceCycle = new ServiceCycleTraceCycleIdentity(
                traceService, 2, generation, generation, generation, generation);
            var capture = new ServiceCycleTraceCaptureIdentity(traceService, 2, generation, generation, generation);
            var timestamp = checked(index * 20L + 1);
            var configuration = ++sequence;
            events.Add(Event(traceSession, configuration, ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticPayload.Publication(false, traceService, generation, timestamp)));
            var attempted = ++sequence;
            events.Add(Event(traceSession, attempted, ServiceCycleSemanticEventKind.StartAttempted,
                ServiceCycleSemanticPayload.StartAttempted(traceService, 2, generation, timestamp + 1), configuration));
            var ready = ++sequence;
            events.Add(Event(traceSession, ready, ServiceCycleSemanticEventKind.StartReady,
                ServiceCycleSemanticPayload.StartReady(
                    traceService, 2, generation, CommonServiceDecisionCodes.Ready.Value, timestamp + 1, 0), attempted));
            var captureStarted = ++sequence;
            events.Add(Event(traceSession, captureStarted, ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, timestamp + 2, 0), ready));
            var strategy = ++sequence;
            events.Add(Event(traceSession, strategy, ServiceCycleSemanticEventKind.StrategyPublished,
                ServiceCycleSemanticPayload.Publication(true, traceService, generation, timestamp + 2), captureStarted));
            var captureCompleted = ++sequence;
            events.Add(Event(traceSession, captureCompleted, ServiceCycleSemanticEventKind.CaptureCompleted,
                ServiceCycleSemanticPayload.CaptureFact(
                    in capture, generation, CommonServiceDecisionCodes.Captured.Value, timestamp + 3, 1), captureStarted));
            var queued = ++sequence;
            events.Add(Event(traceSession, queued, ServiceCycleSemanticEventKind.CycleQueued,
                ServiceCycleSemanticPayload.CycleFact(
                    in traceCycle, CommonServiceDecisionCodes.Ready.Value, timestamp + 4, 1), captureCompleted));
            var started = ++sequence;
            events.Add(Event(traceSession, started, ServiceCycleSemanticEventKind.CycleStarted,
                ServiceCycleSemanticPayload.CycleFact(in traceCycle, 0, timestamp + 5, 0), queued));
            var evaluationStarted = ++sequence;
            events.Add(Event(traceSession, evaluationStarted, ServiceCycleSemanticEventKind.EvaluationStarted,
                ServiceCycleSemanticPayload.Evaluation(in traceCycle, 0, 0, timestamp + 5, 0), started));
            var state = ++sequence;
            events.Add(Event(traceSession, state, ServiceCycleSemanticEventKind.StatePublished,
                ServiceCycleSemanticPayload.State(in traceCycle, generation, fingerprint, timestamp + 6), evaluationStarted));
            var evaluated = ++sequence;
            events.Add(Event(traceSession, evaluated, ServiceCycleSemanticEventKind.EvaluationCompleted,
                ServiceCycleSemanticPayload.EvaluationCompleted(
                    in traceCycle, 0, WakePolicy.Immediate, timestamp + 7, 2), state));
            var published = ++sequence;
            events.Add(Event(traceSession, published, ServiceCycleSemanticEventKind.BatchPublished,
                ServiceCycleSemanticPayload.BatchFact(
                    in traceCycle, generation, 0, 0, 0, 0, -1, 0, 0, 0, 0, timestamp + 8), evaluated));
            var batch = ++sequence;
            events.Add(Event(traceSession, batch, ServiceCycleSemanticEventKind.BatchCompleted,
                ServiceCycleSemanticPayload.BatchFact(
                    in traceCycle, generation, (int)BatchTerminalDisposition.Completed,
                    CommonActionResultCodes.Committed.Value, 0, 0, -1, 0, 0, 0, 0, timestamp + 9), published));
            var completed = ++sequence;
            events.Add(Event(traceSession, completed, ServiceCycleSemanticEventKind.CycleCompleted,
                ServiceCycleSemanticPayload.CycleFact(in traceCycle, 0, timestamp + 9, 0), batch));

            var replayContext = new ServiceCycleReplayContext(
                1, new ServiceCycleContext(identity, default, new MonotonicTimestamp(timestamp + 5)));
            var firstRecord = checked(index * 3L + 1);
            var footer = new ServiceCycleReplayCycleFooter(
                0,
                replayContext,
                ServiceCycleReplayCycleFooterDisposition.Provisional,
                WakePolicy.Immediate,
                true,
                projection,
                true,
                0,
                firstRecord,
                firstRecord + 2,
                3,
                ServiceCycleReplayCompleteness.Complete,
                1,
                Stopwatch.Frequency,
                0);
            Assert.True(session.TryAppendFooter(in footer, out _));
        }
        Assert.True(session.TryReadSnapshot(out var snapshot));
        var semantic = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Count)];
        ServiceCycleTraceCodec.Encode(traceSession, default, 1, events.ToArray(), semantic);
        return ServiceCycleReplayArtifactCodec.Encode(semantic, session, in snapshot);
    }

    private static void Append(
        ServiceCycleReplaySession session,
        in ServiceCycleReplayCycleKey key,
        ServiceCycleReplayCodecDescriptor descriptor,
        byte[] scratch,
        ServiceCycleReplayRecordKind kind,
        int index,
        byte value)
    {
        scratch[0] = value;
        Assert.True(session.TryAppendRecord(
            in key,
            new ServiceCycleReplayRecordIdentity(kind, index),
            in descriptor,
            scratch,
            1,
            out _));
    }

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
