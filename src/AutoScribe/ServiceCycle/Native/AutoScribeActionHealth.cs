namespace OrbAutomata;

using System;

internal sealed class AutoScribeActionHealth
{
    internal bool HasFailure { get; private set; }
    internal AutoScribePreflight Preflight { get; private set; }
    internal AutoScribeNativeStage Stage { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal AutoScribeMutationReceipt Receipt { get; private set; }
    internal Guid RecipeId { get; private set; }
    internal Guid ScrollId { get; private set; }
    internal int Level { get; private set; }
    internal long Revision { get; private set; }

    internal bool Observe(in AutoScribeSubmission submission)
    {
        var action = default(AutoScribeCycleAction);
        return ObserveCore(in action, false, in submission);
    }

    internal bool Observe(
        in AutoScribeCycleAction action,
        in AutoScribeSubmission submission) =>
        ObserveCore(in action, true, in submission);

    private bool ObserveCore(
        in AutoScribeCycleAction action,
        bool hasAction,
        in AutoScribeSubmission submission)
    {
        if (submission.Verified)
        {
            Clear();
            return false;
        }
        if (HasFailure && submission.Preflight == AutoScribePreflight.Quarantined &&
            ScrollId == (hasAction ? action.ScrollId : Guid.Empty))
            return false;
        HasFailure = true;
        Preflight = submission.Preflight;
        Stage = submission.Stage;
        Reason = submission.Reason;
        Receipt = submission.Receipt;
        RecipeId = hasAction ? action.RecipeId : Guid.Empty;
        ScrollId = hasAction ? action.ScrollId : Guid.Empty;
        Level = hasAction ? action.Level : 0;
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
        RecipeId = Guid.Empty;
        ScrollId = Guid.Empty;
        Level = 0;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();
}
