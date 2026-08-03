namespace OrbAutomata;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal AutoScribePreflight Preflight { get; private set; }
    internal AutoScribeNativeStage Stage { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    internal void Observe(in AutoScribeSubmission submission)
    {
        if (submission.Verified)
        {
            Clear();
            return;
        }
        if (!IsFailure(in submission)) return;
        HasFailure = true;
        Preflight = submission.Preflight;
        Stage = submission.Stage;
        Reason = submission.Reason;
        Revision = checked(Revision + 1);
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Preflight = AutoScribePreflight.Proceeded;
        Stage = AutoScribeNativeStage.None;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();

    internal static bool IsFailure(in AutoScribeSubmission submission) =>
        !submission.Verified &&
        (submission.CallOutcome.MutationAttempts > 0 ||
            submission.Preflight is AutoScribePreflight.ContractUnavailable or
                AutoScribePreflight.IdentityUnavailable or
                AutoScribePreflight.RelationshipMismatch or
                AutoScribePreflight.MutationPermitUnavailable or
                AutoScribePreflight.PostPaymentFault or
                AutoScribePreflight.VerificationFailed or
                AutoScribePreflight.Quarantined or
                AutoScribePreflight.WrongThread);
}
