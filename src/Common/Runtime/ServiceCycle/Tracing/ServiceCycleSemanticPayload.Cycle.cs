using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    internal static ServiceCycleSemanticPayload CycleFact(
        in ServiceCycleTraceCycleIdentity identity,
        int code,
        long timestampTicks,
        long durationTicks) =>
        new(
            CycleFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Duration,
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            identity.StrategyGeneration, 0, identity.CycleId, 0, 0, 0,
            timestampTicks, durationTicks, 0, 0, 0, code, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            world: identity.WorldGeneration);

    internal static ServiceCycleSemanticPayload CaptureFact(
        in ServiceCycleTraceCaptureIdentity identity,
        ulong strategyGeneration,
        int code,
        long timestampTicks,
        long durationTicks,
        long frameIdentity) =>
        new(
            CaptureFields |
            (strategyGeneration == 0 ? ServiceCycleSemanticFields.None : ServiceCycleSemanticFields.Strategy) |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration |
            FrameField(frameIdentity),
            identity.Service.Value, identity.LifecycleGeneration, identity.ConfigurationGeneration,
            strategyGeneration, identity.CaptureSequence, identity.CycleId, 0, 0, 0,
            timestampTicks, durationTicks, 0, FrameValue(frameIdentity), 0, code, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload CaptureUnavailable(
        in ServiceCycleTraceCaptureIdentity identity,
        int code,
        WakePolicy wake,
        long timestampTicks,
        long durationTicks,
        long frameIdentity) =>
        WakeOutcome(
            CaptureFields | ServiceCycleSemanticFields.ActionCount,
            identity.Service.Value,
            identity.LifecycleGeneration,
            identity.ConfigurationGeneration,
            identity.CaptureSequence,
            identity.CycleId,
            code,
            wake,
            timestampTicks,
            durationTicks,
            frameIdentity);

    internal static ServiceCycleSemanticPayload StartAttempted(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        long timestampTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Timestamp,
            service.Value, lifecycle, configuration, 0, 0, 0, 0, 0, 0,
            timestampTicks, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload StartDeferred(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        int code,
        WakePolicy wake,
        long timestampTicks,
        long durationTicks) =>
        WakeOutcome(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Configuration,
            service.Value,
            lifecycle,
            configuration,
            0,
            0,
            code,
            wake,
            timestampTicks,
            durationTicks,
            Unframed);

    internal static ServiceCycleSemanticPayload StartReady(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        int code,
        long timestampTicks,
        long durationTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Code |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration,
            service.Value, lifecycle, configuration, 0, 0, 0, 0, 0, 0,
            timestampTicks, durationTicks, 0, 0, 0, code, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload StartFaulted(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong configuration,
        int code,
        int category,
        int occurrence,
        long timestampTicks,
        long durationTicks,
        long retryDueTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Code |
            ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.OccurrenceCount |
            ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration |
            ServiceCycleSemanticFields.Deadline,
            service.Value, lifecycle, configuration, 0, 0, 0, 0, 0, 0,
            timestampTicks, durationTicks, retryDueTicks, 0, 0, code, category, 0,
            0, 0, 0, occurrence, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static ServiceCycleSemanticPayload WakeOutcome(
        ServiceCycleSemanticFields identityFields,
        ulong service,
        ulong lifecycle,
        ulong configuration,
        ulong capture,
        ulong cycle,
        int code,
        WakePolicy wake,
        long timestampTicks,
        long durationTicks,
        long frameIdentity)
    {
        if (!wake.IsValid || wake.Kind is WakePolicyKind.Default or WakePolicyKind.Immediate or
            WakePolicyKind.AfterBatch)
            throw new ArgumentException("A concrete retry wake policy is required.", nameof(wake));
        var operand = wake.Kind == WakePolicyKind.At ? wake.DueTime.Ticks : wake.Delay.Ticks;
        return new ServiceCycleSemanticPayload(
            identityFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Duration | ServiceCycleSemanticFields.Disposition |
            ServiceCycleSemanticFields.Deadline | FrameField(frameIdentity),
            service, lifecycle, configuration, 0, capture, cycle, 0, 0, 0,
            timestampTicks, durationTicks, operand, FrameValue(frameIdentity), 0, code, (int)wake.Kind, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
