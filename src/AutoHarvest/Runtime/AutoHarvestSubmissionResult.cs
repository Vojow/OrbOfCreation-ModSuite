using System;
using OrbModding.Common;

namespace OrbAutomata;

// Numeric values are append-only structured diagnostics contracts.
internal enum AutoHarvestSubmissionFailureCode
{
    None = 0,
    RuntimeReadFailed = 4,
    PolicyRevalidationRejected = 5,
    NativePrerequisitesCurrentlyUnmet = 6,
    NativePrerequisiteValidationUnavailable = 7,
    NativePairIdentityRevalidationRefused = 8,
    NativePlotVisibilityRefused = 9,
    NativeOfferedInstanceMembershipRefused = 10,
    NativeActionRowVisibilityRefused = 11,
    NativeHasEnoughForOneInstanceRefused = 12,
    NativeMaximumRemainingInstancesRefused = 13,
}

/// <summary>The exact latch/check/latch sequence observed before native quantity mutation.</summary>
internal readonly struct AutoHarvestPrerequisiteValidationEvidence
{
    public AutoHarvestPrerequisiteValidationEvidence(
        bool hasBeforeLatch,
        bool beforeLatch,
        bool hasCheckResult,
        bool checkResult,
        bool hasAfterLatch,
        bool afterLatch)
    {
        HasBeforeLatch = hasBeforeLatch;
        BeforeLatch = beforeLatch;
        HasCheckResult = hasCheckResult;
        CheckResult = checkResult;
        HasAfterLatch = hasAfterLatch;
        AfterLatch = afterLatch;
    }

    public bool HasBeforeLatch { get; }
    public bool BeforeLatch { get; }
    public bool HasCheckResult { get; }
    public bool CheckResult { get; }
    public bool HasAfterLatch { get; }
    public bool AfterLatch { get; }
}

internal readonly struct AutoHarvestSubmissionResult
{
    public AutoHarvestSubmissionResult(
        AutoHarvestSubmissionFailureCode failureCode,
        AutoHarvestPrerequisiteValidationEvidence prerequisiteValidation = default)
        : this(false, default, default, failureCode, prerequisiteValidation)
    {
    }

    public AutoHarvestSubmissionResult(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        AutoHarvestPrerequisiteValidationEvidence prerequisiteValidation = default)
        : this(true, outcome, callOutcome, AutoHarvestSubmissionFailureCode.None, prerequisiteValidation)
    {
    }

    private AutoHarvestSubmissionResult(
        bool hasNativeMutationOutcome,
        NativeMutationOutcome nativeMutationOutcome,
        NativeMutationCallOutcome nativeMutationCallOutcome,
        AutoHarvestSubmissionFailureCode failureCode,
        AutoHarvestPrerequisiteValidationEvidence prerequisiteValidation)
    {
        if (hasNativeMutationOutcome && failureCode != AutoHarvestSubmissionFailureCode.None)
            throw new ArgumentException("Native mutation evidence cannot carry a preflight failure code.", nameof(failureCode));
        if (!hasNativeMutationOutcome && failureCode == AutoHarvestSubmissionFailureCode.None)
            throw new ArgumentException("A preflight rejection requires a structured failure code.", nameof(failureCode));
        HasNativeMutationOutcome = hasNativeMutationOutcome;
        NativeMutationOutcome = nativeMutationOutcome;
        NativeMutationCallOutcome = nativeMutationCallOutcome;
        FailureCode = failureCode;
        PrerequisiteValidation = prerequisiteValidation;
    }

    public bool HasNativeMutationOutcome { get; }
    public NativeMutationOutcome NativeMutationOutcome { get; }
    public NativeMutationCallOutcome NativeMutationCallOutcome { get; }
    public AutoHarvestSubmissionFailureCode FailureCode { get; }
    public AutoHarvestPrerequisiteValidationEvidence PrerequisiteValidation { get; }
    public bool Verified => HasNativeMutationOutcome && NativeMutationOutcome == NativeMutationOutcome.Verified;
    public bool MutationAttempted => NativeMutationCallOutcome.MutationAttempts != 0;
}
