using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Names a submission's outcome as an action result.
/// </summary>
/// <remarks>
/// An attempted-but-unverified mutation is the one outcome that must never be retried this
/// lifecycle, and naming it here rather than at the call site is what lets the action boundary
/// quarantine on the code it is about to return instead of re-deriving the same condition.
/// </remarks>
internal static class AutoHarvestActionResultMapper
{
    public static ServiceActionResult FromMutation(in AutoHarvestSubmissionResult mutation)
    {
        if (!mutation.HasNativeMutationOutcome)
        {
            return mutation.FailureCode switch
            {
                AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected =>
                    ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected),
                AutoHarvestSubmissionFailureCode.NativePrerequisitesCurrentlyUnmet =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet),
                AutoHarvestSubmissionFailureCode.NativePrerequisiteValidationUnavailable =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable),
                AutoHarvestSubmissionFailureCode.NativePairIdentityRevalidationRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativePairIdentityRevalidationRefused),
                AutoHarvestSubmissionFailureCode.NativePlotVisibilityRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativePlotVisibilityRefused),
                AutoHarvestSubmissionFailureCode.NativeOfferedInstanceMembershipRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused),
                AutoHarvestSubmissionFailureCode.NativeActionRowVisibilityRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused),
                AutoHarvestSubmissionFailureCode.NativeHasEnoughForOneInstanceRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused),
                AutoHarvestSubmissionFailureCode.NativeMaximumRemainingInstancesRefused =>
                    ServiceActionResult.Rejected(
                        AutoHarvestActionResultCodes.NativeMaximumRemainingInstancesRefused),
                _ => ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault),
            };
        }

        var evidence = ServiceNativeMutationEvidence.Observed(
            mutation.NativeMutationOutcome,
            mutation.NativeMutationCallOutcome);
        if (mutation.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        return ServiceActionResult.Faulted(
            mutation.MutationAttempted
                ? AutoHarvestActionResultCodes.PairFaulted
                : CommonActionResultCodes.AdapterFault,
            evidence);
    }
}
