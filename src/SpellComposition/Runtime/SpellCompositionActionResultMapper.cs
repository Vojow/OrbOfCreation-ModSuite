using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellCompositionActionResultMapper
{
    internal static ServiceActionResult Map(in SpellCompositionSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(
                code,
                ServiceNativeMutationEvidence.Observed(
                    submission.Outcome,
                    submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(SpellCompositionPreflight preflight) => preflight switch
    {
        SpellCompositionPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        SpellCompositionPreflight.ContractUnavailable => SpellCompositionActionResultCodes.ContractUnavailable,
        SpellCompositionPreflight.Quarantined => SpellCompositionActionResultCodes.Quarantined,
        SpellCompositionPreflight.WrongThread => SpellCompositionActionResultCodes.WrongThread,
        SpellCompositionPreflight.IdentityUnavailable => SpellCompositionActionResultCodes.IdentityUnavailable,
        SpellCompositionPreflight.OutputLevelOutOfRange => SpellCompositionActionResultCodes.OutputLevelOutOfRange,
        SpellCompositionPreflight.AlreadyInRequestedState => SpellCompositionActionResultCodes.AlreadyInRequestedState,
        SpellCompositionPreflight.GlyphIdentityUnavailable => SpellCompositionActionResultCodes.GlyphIdentityUnavailable,
        SpellCompositionPreflight.DuplicateGlyph => SpellCompositionActionResultCodes.DuplicateGlyph,
        SpellCompositionPreflight.GlyphUnavailable => SpellCompositionActionResultCodes.GlyphUnavailable,
        SpellCompositionPreflight.NotAnAugment => SpellCompositionActionResultCodes.NotAnAugment,
        SpellCompositionPreflight.UsageLimitExceeded => SpellCompositionActionResultCodes.UsageLimitExceeded,
        SpellCompositionPreflight.IncompatibleComposition => SpellCompositionActionResultCodes.IncompatibleComposition,
        SpellCompositionPreflight.MasteryRequirementUnmet => SpellCompositionActionResultCodes.MasteryRequirementUnmet,
        SpellCompositionPreflight.MutationPermitUnavailable => SpellCompositionActionResultCodes.MutationPermitUnavailable,
        SpellCompositionPreflight.PostCommitFault => SpellCompositionActionResultCodes.PostCommitFault,
        SpellCompositionPreflight.VerificationFailed => SpellCompositionActionResultCodes.VerificationFailed,
        _ => SpellCompositionActionResultCodes.ContractUnavailable,
    };
}
