using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Tests.Runtime.ServiceCycle.Replay.Execution;

internal static class ServiceCycleReplayControlBoundaryFixture
{
    internal static ServiceCycleTraceDocument TimedActionEmergency()
    {
        var session = new ServiceCycleTraceSessionId(951);
        var cycle = new ServiceCycleTraceCycleIdentity(
            new ServiceCycleTraceServiceId(1), 1, 1, 1, 1, 1);
        return Trace(session, new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticPayload.ActionFact(
                    in cycle, 1, 1, 0, 0, 0, null, 0, 0, 0, 11, 0)),
            Event(session, 2, ServiceCycleSemanticEventKind.EmergencyEntered,
                ServiceCycleSemanticPayload.Emergency(
                    (int)EmergencyStopReason.UserRequested, 1, 12), 1),
            Event(session, 3, ServiceCycleSemanticEventKind.PumpCompleted,
                Pump(frameIdentity: 1, actionsAttempted: 1, totalDuration: 3, timestamp: 13)),
        });
    }

    internal static ServiceCycleTraceDocument ConstantClockCaptureLifecycle()
    {
        var session = new ServiceCycleTraceSessionId(952);
        var service = new ServiceCycleTraceServiceId(1);
        var capture = new ServiceCycleTraceCaptureIdentity(service, 1, 1, 1, 1);
        return Trace(session, new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.CaptureStarted,
                ServiceCycleSemanticPayload.CaptureFact(in capture, 0, 0, 10, 0)),
            Event(session, 2, ServiceCycleSemanticEventKind.LifecycleRequested,
                ServiceCycleSemanticPayload.LifecycleFact(service, 2, 0, 10), 1),
            Event(session, 3, ServiceCycleSemanticEventKind.PumpCompleted,
                Pump(frameIdentity: 1, capturesAttempted: 1, timestamp: 10)),
        });
    }

    internal static ServiceCycleTraceDocument ConstantClockBetweenFrameLifecycle()
    {
        var session = new ServiceCycleTraceSessionId(953);
        var service = new ServiceCycleTraceServiceId(1);
        return Trace(session, new[]
        {
            Event(session, 1, ServiceCycleSemanticEventKind.PumpCompleted,
                Pump(frameIdentity: 1, timestamp: 10)),
            Event(session, 2, ServiceCycleSemanticEventKind.LifecycleRequested,
                ServiceCycleSemanticPayload.LifecycleFact(service, 2, 0, 10)),
            Event(session, 3, ServiceCycleSemanticEventKind.PumpCompleted,
                Pump(frameIdentity: 2, timestamp: 10)),
        });
    }

    private static ServiceCycleSemanticPayload Pump(
        long frameIdentity,
        int actionsAttempted = 0,
        int capturesAttempted = 0,
        long totalDuration = 0,
        long timestamp = 10) =>
        ServiceCycleSemanticPayload.Pump(
            frameIdentity,
            accepted: true,
            startingOrdinal: 0,
            responsesAcquired: 0,
            actionsAttempted,
            capturesAttempted,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDuration: 0,
            actionDuration: actionsAttempted == 0 ? 0 : totalDuration,
            captureDuration: capturesAttempted == 0 ? 0 : totalDuration,
            totalDuration,
            timestamp);

    private static ServiceCycleSemanticEvent Event(
        ServiceCycleTraceSessionId session,
        ulong sequence,
        ServiceCycleSemanticEventKind kind,
        ServiceCycleSemanticPayload payload,
        ulong parentSequence = 0)
    {
        var id = new ServiceCycleTraceEventId(session, sequence);
        var parent = parentSequence == 0
            ? default
            : new ServiceCycleTraceEventId(session, parentSequence);
        return new ServiceCycleSemanticEvent(id, parent, kind, in payload);
    }

    private static ServiceCycleTraceDocument Trace(
        ServiceCycleTraceSessionId session,
        ServiceCycleSemanticEvent[] events)
    {
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        ServiceCycleTraceCodec.Encode(session, default, events, bytes);
        return ServiceCycleTraceCodec.Decode(bytes);
    }
}
