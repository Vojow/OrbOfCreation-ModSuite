namespace OrbAutomata;

internal enum AutoHarvestEvidenceState
{
    Unknown,
    Rejected,
    Verified,
}

internal enum AutoHarvestActionSafetyState
{
    Unknown,
    Destructive,
    ResourceDrain,
    UnsafeCompletionEffects,
    NativePhaseCyclePreserving,
}

internal enum AutoHarvestRejectionReason
{
    None,
    IdentityUnverified,
    UnsupportedPair,
    NotSelected,
    PlotVisibilityUnknown,
    PlotNotVisible,
    ActionAvailabilityUnknown,
    ActionUnavailable,
    PrerequisitesUnknown,
    PrerequisitesUnmet,
    ReadinessUnknown,
    NotReady,
    PreservationUnknown,
    DestructiveAction,
    ResourceDrainPresent,
    UnsafeCompletionEffects,
    DuplicateStateUnknown,
    AlreadyQueuedOrRunning,
    ActionSlotStateUnknown,
    NoActionSlot,
}

internal readonly struct AutoHarvestPairDecision
{
    public AutoHarvestPairDecision(bool shouldSubmit, AutoHarvestRejectionReason rejectionReason)
    {
        ShouldSubmit = shouldSubmit;
        RejectionReason = rejectionReason;
    }

    public bool ShouldSubmit { get; }
    public AutoHarvestRejectionReason RejectionReason { get; }
}
