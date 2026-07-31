namespace OrbAutomata;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal AutoScribePreflight Preflight { get; private set; }
    internal AutoScribeNativeStage Stage { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal AutoScribeMutationReceipt Receipt { get; private set; }
    internal long Revision { get; private set; }

    /// <summary>
    /// Records a newly observed failure and returns whether callers should publish or narrate it.
    /// Once the native action boundary has quarantined itself, its zero-mutation rejection is a
    /// consequence of the original fault rather than a replacement for that fault's evidence.
    /// </summary>
    internal bool Observe(in AutoScribeSubmission submission)
    {
        if (submission.Verified)
        {
            Clear();
            return false;
        }
        if (HasFailure && submission.Preflight == AutoScribePreflight.Quarantined)
            return false;
        HasFailure = true;
        Preflight = submission.Preflight;
        Stage = submission.Stage;
        Reason = submission.Reason;
        Receipt = submission.Receipt;
        Revision = checked(Revision + 1);
        return true;
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Preflight = AutoScribePreflight.Proceeded;
        Stage = AutoScribeNativeStage.None;
        Reason = string.Empty;
        Receipt = default;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();
}
