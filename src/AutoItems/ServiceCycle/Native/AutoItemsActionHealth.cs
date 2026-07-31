using System;

namespace OrbAutomata;

/// <summary>
/// Main-thread lifecycle-scoped evidence from the most recent Auto Items action boundary.
/// Exact native and ownership reasons remain available to health/status projection instead
/// of being reduced to a result code.
/// </summary>
internal sealed class AutoItemsActionHealth
{
    internal bool HasFailure { get; private set; }
    internal AutoItemsPreflight Preflight { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    internal void Observe(in AutoItemsSubmission submission)
    {
        if (submission.Verified)
        {
            Clear();
            return;
        }

        var reason = submission.Reason;
        if (string.IsNullOrWhiteSpace(reason))
            reason = $"Auto Items failed at {submission.Preflight}.";
        HasFailure = true;
        Preflight = submission.Preflight;
        Reason = reason;
        Revision = checked(Revision + 1);
    }

    internal void ObserveOwnership(string reason)
    {
        HasFailure = true;
        Preflight = AutoItemsPreflight.MutationPermitUnavailable;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? "Auto Items does not own the complete consumable-use transaction."
            : reason;
        Revision = checked(Revision + 1);
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Preflight = AutoItemsPreflight.Proceeded;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();
}
