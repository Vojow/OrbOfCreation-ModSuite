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
}
