using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestActionResultMapper
{
    public static ServiceActionResult FromMutation(in AutoHarvestSubmissionResult mutation)
    {
        if (!mutation.HasNativeMutationOutcome)
        {
            return mutation.FailureCode == AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected
                ? ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected)
                : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(
            mutation.NativeMutationOutcome,
            mutation.NativeMutationCallOutcome);
        return mutation.Verified
            ? ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence)
            : ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }
}
