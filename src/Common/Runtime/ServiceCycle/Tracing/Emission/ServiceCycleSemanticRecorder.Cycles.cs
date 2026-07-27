using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

public sealed partial class ServiceCycleSemanticRecorder
{
    public void CycleQueued(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _cycles.CycleQueued(ordinal, in cycle, in decision, observedAt, duration);

    public void CycleStarted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _cycles.CycleStarted(ordinal, in cycle, observedAt, duration);

    public void CycleCompleted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _cycles.CycleCompleted(ordinal, in cycle, observedAt, duration);

    public void CycleOrphaned(
        int ordinal,
        in ServiceCycleIdentity cycle,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _cycles.CycleOrphaned(ordinal, in cycle, observedAt, duration);

    public void CycleFaulted(
        int ordinal,
        in ServiceCycleIdentity cycle,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _cycles.CycleFaulted(ordinal, in cycle, in fault, observedAt, duration);

    public void CaptureStarted(int ordinal, in ServiceCaptureContext capture) =>
        _admission.CaptureStarted(ordinal, in capture);

    public void StartAttempted(
        int ordinal,
        in ServiceCycleStartContext context,
        MonotonicTimestamp observedAt) =>
        _admission.StartAttempted(ordinal, in context, observedAt);

    public void StartDeferred(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _admission.StartDeferred(ordinal, in context, in decision, observedAt, duration);

    public void StartReady(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _admission.StartReady(ordinal, in context, in decision, observedAt, duration);

    public void StartFaulted(
        int ordinal,
        in ServiceCycleStartContext context,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration,
        MonotonicTimestamp retryDue) =>
        _admission.StartFaulted(ordinal, in context, in fault, observedAt, duration, retryDue);

    public void CaptureCompleted(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceCaptureResult result,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _admission.CaptureCompleted(ordinal, in capture, in result, observedAt, duration);

    public void CaptureUnavailable(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceCaptureResult result,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _admission.CaptureUnavailable(ordinal, in capture, in result, observedAt, duration);

    public void CaptureFaulted(
        int ordinal,
        in ServiceCaptureContext capture,
        in ServiceFault fault,
        MonotonicTimestamp observedAt,
        MonotonicDuration duration) =>
        _admission.CaptureFaulted(ordinal, in capture, in fault, observedAt, duration);
}
