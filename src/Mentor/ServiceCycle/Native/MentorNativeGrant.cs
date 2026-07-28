using OrbModding.Common;

namespace OrbMentor;

internal enum MentorNativeGrantStatus
{
    Committed,
    RecipientIneligible,
    IdentityChanged,
    ContractUnavailable,
    PostconditionFailed,
}

internal readonly struct MentorNativeGrant
{
    internal MentorNativeGrant(
        MentorNativeGrantStatus status,
        string reason,
        NativeMutationOutcome outcome = NativeMutationOutcome.BeforeCaptureFailed,
        NativeMutationCallOutcome callOutcome = default)
    {
        Status = status;
        Reason = reason;
        Outcome = outcome;
        CallOutcome = callOutcome;
    }

    internal MentorNativeGrantStatus Status { get; }
    internal string Reason { get; }
    internal NativeMutationOutcome Outcome { get; }
    internal NativeMutationCallOutcome CallOutcome { get; }
}
