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

    internal bool Observe(in AutoItemsSubmission submission)
    {
        if (submission.Verified)
        {
            return Clear();
        }

        var reason = submission.Reason;
        if (string.IsNullOrWhiteSpace(reason))
            reason = $"Auto Items failed at {submission.Preflight}.";
        if (HasFailure && Preflight == submission.Preflight &&
            string.Equals(Reason, reason, StringComparison.Ordinal))
            return false;
        HasFailure = true;
        Preflight = submission.Preflight;
        Reason = reason;
        Revision = checked(Revision + 1);
        return true;
    }

    internal bool ObserveOwnership(string reason)
    {
        reason = string.IsNullOrWhiteSpace(reason)
            ? "Auto Items does not own the complete consumable-use transaction."
            : reason;
        if (HasFailure && Preflight == AutoItemsPreflight.MutationPermitUnavailable &&
            string.Equals(Reason, reason, StringComparison.Ordinal))
            return false;
        HasFailure = true;
        Preflight = AutoItemsPreflight.MutationPermitUnavailable;
        Reason = reason;
        Revision = checked(Revision + 1);
        return true;
    }

    internal bool Clear()
    {
        if (!HasFailure && Reason.Length == 0) return false;
        HasFailure = false;
        Preflight = AutoItemsPreflight.Proceeded;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
        return true;
    }

    internal bool ClearTransient()
    {
        if (!HasFailure || Preflight is AutoItemsPreflight.ContractUnavailable or
            AutoItemsPreflight.MultiBuyUnavailable or AutoItemsPreflight.Quarantined)
            return false;
        return Clear();
    }

    internal void InvalidateLifecycle() => Clear();
}
