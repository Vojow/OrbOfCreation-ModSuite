namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    /// <summary>
    /// A publication is suite-wide: one configuration record and one strategy bulletin that every
    /// service reads, so the event names a generation and no service.
    /// </summary>
    internal static ServiceCycleSemanticPayload Publication(
        bool strategy,
        ulong generation,
        long timestampTicks) =>
        new(
            strategy
                ? ServiceCycleSemanticFields.Strategy | ServiceCycleSemanticFields.Timestamp
                : ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Timestamp,
            0, 0, strategy ? 0 : generation, strategy ? generation : 0, 0, 0, 0, 0, 0,
            timestampTicks, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload LifecycleFact(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        int code,
        long timestampTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp,
            service.Value, lifecycle, 0, 0, 0, 0, 0, 0, 0, timestampTicks, 0, 0, 0, 0, code, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload LifecycleConstructionDeferred(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        int code,
        long timestampTicks,
        long deadlineTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Deadline,
            service: service.Value,
            lifecycle: lifecycle,
            configuration: 0,
            strategy: 0,
            capture: 0,
            cycle: 0,
            batch: 0,
            action: 0,
            statePublication: 0,
            timestampTicks: timestampTicks,
            durationTicks: 0,
            deadlineTicks: deadlineTicks,
            frameIdentity: 0,
            fingerprint: 0,
            code: code,
            disposition: 0,
            actionIndex: 0,
            actionCount: 0,
            committedCount: 0,
            untouchedSuffixCount: 0,
            occurrenceCount: 0,
            nativeCallsAttempted: 0,
            mutationAttempts: 0,
            mutationsCommitted: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: 0,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDurationTicks: 0,
            actionDurationTicks: 0,
            captureDurationTicks: 0,
            totalDurationTicks: 0,
            nativeOutcome: 0);

    internal static ServiceCycleSemanticPayload Emergency(int reason, int occurrence, long timestampTicks) =>
        new(
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.OccurrenceCount |
            ServiceCycleSemanticFields.Timestamp,
            0, 0, 0, 0, 0, 0, 0, 0, 0, timestampTicks, 0, 0, 0, 0, reason, 0, 0,
            0, 0, 0, occurrence, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload FaultOrRetry(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        int category,
        int code,
        int occurrence,
        long timestampTicks,
        long deadlineTicks) =>
        new(
            ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
            ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Code |
            ServiceCycleSemanticFields.OccurrenceCount | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Deadline,
            service.Value, lifecycle, 0, 0, 0, 0, 0, 0, 0, timestampTicks, 0, deadlineTicks, 0, 0,
            code, category, 0, 0, 0, 0, occurrence, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    internal static ServiceCycleSemanticPayload Pump(
        long frameIdentity,
        bool accepted,
        int startingOrdinal,
        int responsesAcquired,
        int actionsAttempted,
        int capturesAttempted,
        int cyclesStarted,
        int worldGateDeferrals,
        int emergencyBatchesRejected,
        long lifecycleTransitions,
        long responseDuration,
        long actionDuration,
        long captureDuration,
        long totalDuration,
        long timestampTicks) =>
        new(
            ServiceCycleSemanticFields.FrameIdentity | ServiceCycleSemanticFields.PumpCounts |
            ServiceCycleSemanticFields.PumpDurations | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionIndex,
            0, 0, 0, 0, 0, 0, 0, 0, 0, timestampTicks, 0, 0, frameIdentity, 0, accepted ? 1 : 0, 0, startingOrdinal,
            0, 0, 0, 0, 0, 0, 0, responsesAcquired, actionsAttempted, capturesAttempted,
            emergencyBatchesRejected, lifecycleTransitions, responseDuration, actionDuration, captureDuration, totalDuration, 0,
            cyclesStarted: cyclesStarted,
            worldGateDeferrals: worldGateDeferrals);
}
