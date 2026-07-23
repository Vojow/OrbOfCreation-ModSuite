using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Format;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

public sealed class ServiceCycleReplayProductionEvidenceRepairTests
{
    [Fact]
    public void DenseProjectionPreservesMutatedPumpAggregateForExactComparison()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.CaptureTwoServices();
        var original = captured.Artifact;
        var mutated = WithMutatedPump(original);
        var participants = new IServiceCycleReplayProductionParticipant[]
        {
            new Participant(1),
            new Participant(2),
        };
        var map = new ServiceCycleReplayTraceMap(participants);

        var expected = ServiceCycleReplaySemanticProjection.Create(mutated, map);
        var actual = ServiceCycleReplaySemanticProjection.Create(original, map);
        var mismatch = ServiceCycleReplaySemanticComparer.Compare(
            original.GetCycle(0).Key, expected, actual);

        Assert.True(mismatch.HasValue);
        Assert.Equal(ServiceCycleReplayMismatchCode.SemanticEvent, mismatch.Value.Mismatch.Code);
        Assert.Equal(31, mismatch.Value.Mismatch.FieldCode);
    }

    [Fact]
    public void ProductionReplayRejectsChecksumValidMutatedPumpTiming()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(3, varyingClock: true);
        var mutated = WithMutatedPumpTiming(captured.Artifact);
        var factory = new Factory(3);
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var result = ServiceCycleReplayProductionDriver.Run(
            mutated, registration, factory, TimeSpan.FromSeconds(2));

        Assert.True(mutated.IsComplete);
        Assert.False(result.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ClockEvidenceRejected,
            result.Failure.Fault.DetailCode);
    }

    [Theory]
    [InlineData(ServiceCycleSemanticEventKind.StartAttempted)]
    [InlineData(ServiceCycleSemanticEventKind.ActionAttempted)]
    [InlineData(ServiceCycleSemanticEventKind.CaptureStarted)]
    public void ConfigurationPublicationInsideCallbackBoundaryIsRejected(
        ServiceCycleSemanticEventKind boundaryKind)
    {
        var semantic = CallbackConfigurationTrace(boundaryKind);

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(semantic, new[] { 1 });

        Assert.True(failure.IsValid);
        Assert.Equal(ServiceCycleSemanticEventKind.ConfigurationPublished, failure.ControlKind);
        Assert.Equal(boundaryKind, semantic[failure.OwnerEventIndex].Kind);
        Assert.Equal(1, failure.TraceServiceKey);
    }

    [Theory]
    [InlineData(ServiceCycleSemanticEventKind.StartAttempted)]
    [InlineData(ServiceCycleSemanticEventKind.ActionAttempted)]
    [InlineData(ServiceCycleSemanticEventKind.CaptureStarted)]
    public void NonCaptureStrategyPublicationInsideCallbackBoundaryIsControlOrderRejected(
        ServiceCycleSemanticEventKind boundaryKind)
    {
        var semantic = CallbackStrategyTrace(boundaryKind);

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(semantic, new[] { 1 });

        Assert.True(failure.IsValid);
        Assert.Equal(ServiceCycleSemanticEventKind.StrategyPublished, failure.ControlKind);
        Assert.Equal(ServiceCycleReplayExecutionDetailCode.ControlOrderRejected, failure.Detail);
        Assert.Equal(boundaryKind, semantic[failure.OwnerEventIndex].Kind);
    }

    [Fact]
    public void CaptureDerivedStrategyPublicationIsNotAReplayMutationControl()
    {
        var semantic = CaptureDerivedStrategyTrace();

        var failure = ServiceCycleReplayControlBoundaryValidator.Validate(semantic, new[] { 1 });

        Assert.False(failure.IsValid);
    }

    [Fact]
    public void EmptyIncompleteArtifactReturnsStableTypedPreflightFailure()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var artifact = Clone(
            captured.Artifact,
            captured.Artifact.SemanticTrace,
            Array.Empty<ServiceCycleReplayArtifactCycle>(),
            ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete);

        var plan = new ServiceCycleReplayProductionArtifactPlan(artifact);
        var result = ServiceCycleReplayProductionPreflight.Validate(
            plan, Array.Empty<IServiceCycleReplayExecutionRegistration?>());

        Assert.True(result.HasValue);
        Assert.False(result.Value.Succeeded);
        Assert.True(result.Value.Failure.Cycle.IsValid);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            result.Value.Failure.Fault.DetailCode);
    }

    [Fact]
    public void DetachedEvaluatorReturnsTypedFailureForEmptyIncompleteArtifact()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var artifact = Clone(
            captured.Artifact,
            captured.Artifact.SemanticTrace,
            Array.Empty<ServiceCycleReplayArtifactCycle>(),
            ServiceCycleReplayArtifactEligibilityCode.SemanticJoinIncomplete);
        var factory = new Factory();
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var result = registration.VerifyEvaluator(artifact);

        Assert.False(result.Succeeded);
        Assert.True(result.Failure.Cycle.IsValid);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ArtifactNotComplete,
            result.Failure.Fault.DetailCode);
        Assert.Equal(0, factory.CreationCount);
    }

    [Fact]
    public void CoalescedConfigurationGenerationIsRejectedBeforeFactoryCreation()
    {
        var captured = ServiceCycleReplayProductionScenarioFixture.Capture(0);
        var artifact = WithConfigurationGap(captured.Artifact);
        var factory = new Factory();
        var registration = new ServiceCycleReplayExecutionRegistration<
            Frame, Config, State, Action, InputRecord, StateRecord, ActionRecord>(1, factory);

        var plan = new ServiceCycleReplayProductionArtifactPlan(artifact);
        using var participant =
            ((IServiceCycleReplayExecutionRegistration)registration).PrepareProduction(plan);

        Assert.False(participant.Preparation.Succeeded);
        Assert.Equal(
            (int)ServiceCycleReplayExecutionDetailCode.ConfigurationEvidenceMissing,
            participant.Preparation.Failure.Fault.DetailCode);
        Assert.Equal(0, factory.CreationCount);
    }

    private static ServiceCycleReplayArtifactDocument WithMutatedPump(
        ServiceCycleReplayArtifactDocument source)
    {
        var trace = source.SemanticTrace;
        var events = trace.Events.ToArray();
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted) continue;
            var p = item.Payload;
            var changed = ServiceCycleSemanticPayload.Pump(
                p.FrameIdentity,
                p.PumpAccepted,
                p.StartingOrdinal,
                p.ResponsesAcquired,
                checked(p.ActionsAttempted + 1),
                p.CapturesAttempted,
                p.EmergencyBatchesRejected,
                p.LifecycleTransitions,
                p.ResponseDurationTicks,
                p.ActionDurationTicks,
                p.CaptureDurationTicks,
                p.TotalDurationTicks,
                p.TimestampTicks);
            events[index] = new ServiceCycleSemanticEvent(item.Id, item.Parent, item.Kind, in changed);
            break;
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(
            trace.Session, trace.Dropped, trace.ServiceCapacity, events, bytes);
        var semantic = ServiceCycleTraceCodec.Decode(bytes);
        var cycles = new ServiceCycleReplayArtifactCycle[source.CycleCount];
        for (var index = 0; index < cycles.Length; index++) cycles[index] = source.GetCycle(index);
        return Clone(source, semantic, cycles, source.Eligibility);
    }

    private static ServiceCycleReplayArtifactDocument WithMutatedPumpTiming(
        ServiceCycleReplayArtifactDocument source)
    {
        var trace = source.SemanticTrace;
        var events = trace.Events.ToArray();
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            if (item.Kind != ServiceCycleSemanticEventKind.PumpCompleted) continue;
            var p = item.Payload;
            var changed = ServiceCycleSemanticPayload.Pump(
                p.FrameIdentity,
                p.PumpAccepted,
                p.StartingOrdinal,
                p.ResponsesAcquired,
                p.ActionsAttempted,
                p.CapturesAttempted,
                p.EmergencyBatchesRejected,
                p.LifecycleTransitions,
                p.ResponseDurationTicks,
                checked(p.ActionDurationTicks + 1),
                p.CaptureDurationTicks,
                p.TotalDurationTicks,
                p.TimestampTicks);
            events[index] = new ServiceCycleSemanticEvent(item.Id, item.Parent, item.Kind, in changed);
            break;
        }
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(
            trace.Session, trace.Dropped, trace.ServiceCapacity, events, bytes);
        var semantic = ServiceCycleTraceCodec.Decode(bytes);
        var cycles = new ServiceCycleReplayArtifactCycle[source.CycleCount];
        for (var index = 0; index < cycles.Length; index++) cycles[index] = source.GetCycle(index);
        return Clone(source, semantic, cycles, source.Eligibility);
    }

    private static ServiceCycleReplayArtifactDocument WithConfigurationGap(
        ServiceCycleReplayArtifactDocument source)
    {
        var trace = source.SemanticTrace;
        var events = new ServiceCycleSemanticEvent[trace.Count + 1];
        for (var index = 0; index < trace.Count; index++) events[index] = trace[index];
        var service = new ServiceCycleTraceServiceId(1);
        var timestamp = trace.Count == 0 ? 0 : trace[^1].Payload.TimestampTicks;
        var payload = ServiceCycleSemanticPayload.Publication(false, service, 3, timestamp);
        events[^1] = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(trace.Session, checked((ulong)events.Length)),
            default,
            ServiceCycleSemanticEventKind.ConfigurationPublished,
            in payload);
        var semantic = new ServiceCycleTraceDocument(
            trace.SchemaVersion, trace.Session, trace.Dropped, trace.ServiceCapacity, events);
        var cycles = new ServiceCycleReplayArtifactCycle[source.CycleCount];
        for (var index = 0; index < cycles.Length; index++) cycles[index] = source.GetCycle(index);
        return Clone(source, semantic, cycles, source.Eligibility);
    }

    private static ServiceCycleReplayArtifactDocument Clone(
        ServiceCycleReplayArtifactDocument source,
        ServiceCycleTraceDocument semantic,
        ServiceCycleReplayArtifactCycle[] cycles,
        ServiceCycleReplayArtifactEligibilityCode eligibility)
    {
        var codecs = new ServiceCycleReplayCodecManifestEntry[source.CodecCount];
        for (var index = 0; index < codecs.Length; index++) codecs[index] = source.GetCodec(index);
        return new ServiceCycleReplayArtifactDocument(
            source.Prepared,
            source.Fence,
            semantic,
            source.Recording,
            codecs,
            ServiceCycleReplayCodecIndex.Build(codecs),
            cycles,
            eligibility);
    }

    private static ServiceCycleTraceDocument CallbackConfigurationTrace(
        ServiceCycleSemanticEventKind boundaryKind)
    {
        var session = new ServiceCycleTraceSessionId(981);
        var service = new ServiceCycleTraceServiceId(1);
        var cycle = new ServiceCycleTraceCycleIdentity(service, 1, 1, 1, 1, 1);
        var capture = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        var boundary = boundaryKind switch
        {
            ServiceCycleSemanticEventKind.StartAttempted =>
                ServiceCycleSemanticPayload.StartAttempted(service, 1, 1, 10),
            ServiceCycleSemanticEventKind.ActionAttempted =>
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 1, 1, 0, 0, 0, null, 0, 0, 0, 10, 0),
            ServiceCycleSemanticEventKind.CaptureStarted =>
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 10, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(boundaryKind)),
        };
        var first = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 1), default, boundaryKind, in boundary);
        var publication = ServiceCycleSemanticPayload.Publication(false, service, 2, 10);
        var second = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 2), first.Id,
            ServiceCycleSemanticEventKind.ConfigurationPublished, in publication);
        var pump = ServiceCycleSemanticPayload.Pump(
            1, true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10);
        var third = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 3), default,
            ServiceCycleSemanticEventKind.PumpCompleted, in pump);
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(3)];
        ServiceCycleTraceCodec.Encode(session, default, 1, new[] { first, second, third }, bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private static ServiceCycleTraceDocument CallbackStrategyTrace(
        ServiceCycleSemanticEventKind boundaryKind)
    {
        var session = new ServiceCycleTraceSessionId(982);
        var service = new ServiceCycleTraceServiceId(1);
        var cycle = new ServiceCycleTraceCycleIdentity(service, 1, 1, 1, 1, 1);
        var capture = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        var boundary = boundaryKind switch
        {
            ServiceCycleSemanticEventKind.StartAttempted =>
                ServiceCycleSemanticPayload.StartAttempted(service, 1, 1, 10),
            ServiceCycleSemanticEventKind.ActionAttempted =>
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 1, 1, 0, 0, 0, null, 0, 0, 0, 10, 0),
            ServiceCycleSemanticEventKind.CaptureStarted =>
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 10, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(boundaryKind)),
        };
        var first = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 1), default, boundaryKind, in boundary);
        var publication = ServiceCycleSemanticPayload.Publication(true, service, 2, 11);
        var second = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 2), first.Id,
            ServiceCycleSemanticEventKind.StrategyPublished, in publication);
        var pump = ServiceCycleSemanticPayload.Pump(
            1, true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 12);
        var third = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 3), default,
            ServiceCycleSemanticEventKind.PumpCompleted, in pump);
        return Trace(session, new[] { first, second, third });
    }

    private static ServiceCycleTraceDocument CaptureDerivedStrategyTrace()
    {
        var session = new ServiceCycleTraceSessionId(983);
        var service = new ServiceCycleTraceServiceId(1);
        var capture = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        var startedPayload = ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 10, 0);
        var started = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 1), default,
            ServiceCycleSemanticEventKind.CaptureStarted, in startedPayload);
        var publicationPayload = ServiceCycleSemanticPayload.Publication(true, service, 2, 12);
        var publication = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 2), started.Id,
            ServiceCycleSemanticEventKind.StrategyPublished, in publicationPayload);
        var completedPayload = ServiceCycleSemanticPayload.CaptureFact(
            in capture, 2, CommonServiceDecisionCodes.Captured.Value, 12, 2);
        var completed = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 3), started.Id,
            ServiceCycleSemanticEventKind.CaptureCompleted, in completedPayload);
        var pumpPayload = ServiceCycleSemanticPayload.Pump(
            1, true, 0, 0, 0, 1, 0, 0, 0, 0, 2, 3, 13);
        var pump = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(session, 4), default,
            ServiceCycleSemanticEventKind.PumpCompleted, in pumpPayload);
        return Trace(session, new[] { started, publication, completed, pump });
    }

    private static ServiceCycleTraceDocument Trace(
        ServiceCycleTraceSessionId session,
        ServiceCycleSemanticEvent[] events)
    {
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, 1, events, bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }

    private sealed class Participant : IServiceCycleReplayProductionParticipant
    {
        internal Participant(int traceServiceKey) => TraceServiceKey = traceServiceKey;
        public int TraceServiceKey { get; }
        public int CycleCount => 0;
        public ServiceCycleReplayCycleKey FirstCycle => default;
        public ServiceCycleReplayExecutionResult Preparation => default;
        public bool NativeComplete => false;
        public bool CaptureEvidenceComplete => false;
        public bool TryRegister(ServiceCycleRegistry registry, ServiceCycleReplaySession recording) => false;
        public void RegisterWorkerSchedules(
            ServiceCycleReplayClockScript clock,
            ServiceCycleReplayProductionArtifactPlan plan,
            LifecycleGeneration initialLifecycle) { }
        public void PreparePump(ServiceCycleReplayPumpPlan pumpPlan) { }
        public bool WaitForWorkerReady(TimeSpan timeout) => false;
        public bool WaitForResponseReadyAndWorkerSettled(
            ServiceCycleReplayCycleKey expectedCycle,
            TimeSpan timeout) => false;
        public bool TryPublishConfiguration(ulong generation) => false;
        public bool TryPublishStrategy(ulong generation) => false;
        public void DisposeAndWait(TimeSpan workerBoundaryTimeout) => Dispose();
        public void Dispose() { }
    }
}
