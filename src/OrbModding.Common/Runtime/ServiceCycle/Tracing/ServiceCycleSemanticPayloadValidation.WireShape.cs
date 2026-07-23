namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static partial class ServiceCycleSemanticPayloadValidation
{
    private const ServiceCycleSemanticFields PublicationFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields LifecycleFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
        ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields LifecycleDeferralFields =
        LifecycleFields | ServiceCycleSemanticFields.Deadline;
    private const ServiceCycleSemanticFields EmergencyFields =
        ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.OccurrenceCount |
        ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields CycleFactFields =
        ServiceCycleSemanticPayload.CycleFields | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.Timestamp | ServiceCycleSemanticFields.Duration;
    private const ServiceCycleSemanticFields CaptureFactFields =
        ServiceCycleSemanticPayload.CaptureFields | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.ActionCount | ServiceCycleSemanticFields.Timestamp |
        ServiceCycleSemanticFields.Duration;
    private const ServiceCycleSemanticFields StartAttemptFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
        ServiceCycleSemanticFields.Configuration | ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields StartDeferredFields =
        StartAttemptFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Duration |
        ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Deadline;
    private const ServiceCycleSemanticFields StartReadyFields =
        StartAttemptFields | ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Duration;
    private const ServiceCycleSemanticFields StartFaultFields =
        StartDeferredFields | ServiceCycleSemanticFields.OccurrenceCount;
    private const ServiceCycleSemanticFields EvaluationFactFields =
        ServiceCycleSemanticPayload.CycleFields | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.ActionCount | ServiceCycleSemanticFields.Timestamp |
        ServiceCycleSemanticFields.Duration;
    private const ServiceCycleSemanticFields StateFields =
        ServiceCycleSemanticPayload.CycleFields | ServiceCycleSemanticFields.StatePublication |
        ServiceCycleSemanticFields.Fingerprint | ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields BatchFields =
        ServiceCycleSemanticPayload.CycleFields | ServiceCycleSemanticFields.Batch |
        ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.ActionCount | ServiceCycleSemanticFields.CommittedCount |
        ServiceCycleSemanticFields.ActionIndex | ServiceCycleSemanticFields.UntouchedSuffixCount |
        ServiceCycleSemanticFields.NativeCallTotals | ServiceCycleSemanticFields.Timestamp;
    private const ServiceCycleSemanticFields ActionFields =
        ServiceCycleSemanticPayload.CycleFields | ServiceCycleSemanticFields.Batch |
        ServiceCycleSemanticFields.Action | ServiceCycleSemanticFields.ActionIndex |
        ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.NativeCallTotals | ServiceCycleSemanticFields.Timestamp |
        ServiceCycleSemanticFields.Duration;
    private const ServiceCycleSemanticFields FaultFields =
        ServiceCycleSemanticFields.Service | ServiceCycleSemanticFields.Lifecycle |
        ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Code |
        ServiceCycleSemanticFields.OccurrenceCount | ServiceCycleSemanticFields.Timestamp |
        ServiceCycleSemanticFields.Deadline;
    private const ServiceCycleSemanticFields PumpFields =
        ServiceCycleSemanticFields.FrameIdentity | ServiceCycleSemanticFields.PumpCounts |
        ServiceCycleSemanticFields.PumpDurations | ServiceCycleSemanticFields.Timestamp |
        ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionIndex;

    private static ServiceCycleSemanticFields ExpectedFields(ServiceCycleSemanticEventKind kind) => kind switch
    {
        ServiceCycleSemanticEventKind.ConfigurationPublished => PublicationFields | ServiceCycleSemanticFields.Configuration,
        ServiceCycleSemanticEventKind.StrategyPublished => PublicationFields | ServiceCycleSemanticFields.Strategy,
        ServiceCycleSemanticEventKind.LifecycleRequested or ServiceCycleSemanticEventKind.LifecycleActivated or
            ServiceCycleSemanticEventKind.LifecycleRetired => LifecycleFields,
        ServiceCycleSemanticEventKind.LifecycleConstructionDeferred => LifecycleDeferralFields,
        ServiceCycleSemanticEventKind.EmergencyEntered or ServiceCycleSemanticEventKind.EmergencyCleared => EmergencyFields,
        ServiceCycleSemanticEventKind.CycleQueued or ServiceCycleSemanticEventKind.CycleStarted or
            ServiceCycleSemanticEventKind.CycleCompleted or ServiceCycleSemanticEventKind.CycleOrphaned or
            ServiceCycleSemanticEventKind.CycleFaulted => CycleFactFields,
        ServiceCycleSemanticEventKind.CaptureCompleted => CaptureFactFields | ServiceCycleSemanticFields.Strategy,
        ServiceCycleSemanticEventKind.CaptureStarted or ServiceCycleSemanticEventKind.CaptureFaulted => CaptureFactFields,
        ServiceCycleSemanticEventKind.CaptureUnavailable =>
            CaptureFactFields | ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Deadline,
        ServiceCycleSemanticEventKind.EvaluationCompleted or ServiceCycleSemanticEventKind.ProjectionFaulted =>
            EvaluationFactFields | ServiceCycleSemanticFields.Disposition | ServiceCycleSemanticFields.Deadline,
        ServiceCycleSemanticEventKind.EvaluationStarted or ServiceCycleSemanticEventKind.EvaluationFaulted =>
            EvaluationFactFields,
        ServiceCycleSemanticEventKind.EvaluationDeferred =>
            EvaluationFactFields | ServiceCycleSemanticFields.Deadline,
        ServiceCycleSemanticEventKind.StatePublished => StateFields,
        ServiceCycleSemanticEventKind.BatchPublished or ServiceCycleSemanticEventKind.BatchCompleted or
            ServiceCycleSemanticEventKind.BatchAborted or ServiceCycleSemanticEventKind.BatchOrphaned => BatchFields,
        ServiceCycleSemanticEventKind.ActionCommitted => ActionFields | ServiceCycleSemanticFields.NativeMutationOutcome,
        ServiceCycleSemanticEventKind.ActionAttempted or ServiceCycleSemanticEventKind.ActionRejected or
            ServiceCycleSemanticEventKind.ActionFaulted => ActionFields,
        ServiceCycleSemanticEventKind.RetryScheduled or ServiceCycleSemanticEventKind.FaultObserved or
            ServiceCycleSemanticEventKind.FaultRecovered => FaultFields,
        ServiceCycleSemanticEventKind.PumpCompleted => PumpFields,
        ServiceCycleSemanticEventKind.StartAttempted => StartAttemptFields,
        ServiceCycleSemanticEventKind.StartDeferred => StartDeferredFields,
        ServiceCycleSemanticEventKind.StartFaulted => StartFaultFields,
        ServiceCycleSemanticEventKind.StartReady => StartReadyFields,
        _ => ServiceCycleSemanticFields.None,
    };

    private static void EnsureUnusedFieldsAreZero(in ServiceCycleSemanticPayload p)
    {
        var f = p.Fields;
        Require((Has(f, ServiceCycleSemanticFields.Service) || p.Service == 0) &&
            (Has(f, ServiceCycleSemanticFields.Lifecycle) || p.Lifecycle == 0) &&
            (Has(f, ServiceCycleSemanticFields.Configuration) || p.Configuration == 0) &&
            (Has(f, ServiceCycleSemanticFields.Strategy) || p.Strategy == 0) &&
            (Has(f, ServiceCycleSemanticFields.Capture) || p.Capture == 0) &&
            (Has(f, ServiceCycleSemanticFields.Cycle) || p.Cycle == 0) &&
            (Has(f, ServiceCycleSemanticFields.Batch) || p.Batch == 0) &&
            (Has(f, ServiceCycleSemanticFields.Action) || p.Action == 0) &&
            (Has(f, ServiceCycleSemanticFields.StatePublication) || p.StatePublication == 0) &&
            (Has(f, ServiceCycleSemanticFields.Duration) || p.DurationTicks == 0) &&
            (Has(f, ServiceCycleSemanticFields.Deadline) || p.DeadlineTicks == 0) &&
            (Has(f, ServiceCycleSemanticFields.FrameIdentity) || p.FrameIdentity == 0) &&
            (Has(f, ServiceCycleSemanticFields.Fingerprint) || p.Fingerprint == 0) &&
            (Has(f, ServiceCycleSemanticFields.Code) || p.Code == 0) &&
            (Has(f, ServiceCycleSemanticFields.Disposition) || p.Disposition == 0) &&
            (Has(f, ServiceCycleSemanticFields.ActionIndex) || p.ActionIndex == 0) &&
            (Has(f, ServiceCycleSemanticFields.ActionCount) || p.ActionCount == 0) &&
            (Has(f, ServiceCycleSemanticFields.CommittedCount) || p.CommittedCount == 0) &&
            (Has(f, ServiceCycleSemanticFields.UntouchedSuffixCount) || p.UntouchedSuffixCount == 0) &&
            (Has(f, ServiceCycleSemanticFields.OccurrenceCount) || p.OccurrenceCount == 0) &&
            (Has(f, ServiceCycleSemanticFields.NativeMutationOutcome) || p.NativeOutcomeCode == 0) &&
            (Has(f, ServiceCycleSemanticFields.NativeCallTotals) || NativeTotalsAreZero(in p)) &&
            (Has(f, ServiceCycleSemanticFields.PumpCounts) || p.ResponsesAcquired == 0 && p.ActionsAttempted == 0 &&
                p.CapturesAttempted == 0 && p.EmergencyBatchesRejected == 0 && p.LifecycleTransitions == 0) &&
            (Has(f, ServiceCycleSemanticFields.PumpDurations) || p.ResponseDurationTicks == 0 &&
                p.ActionDurationTicks == 0 && p.CaptureDurationTicks == 0 && p.TotalDurationTicks == 0), nameof(p));
    }
}
