using System;
using System.Collections.Generic;

namespace OrbAutomata;

/// <summary>
/// Detached evidence about one exact member of the game's stacked action queue.
/// </summary>
/// <remarks>
/// This is observation, not admission authority. The recovery boundary must resolve the UUID and
/// exact native type again and capture all counts immediately before any mutation.
/// </remarks>
internal readonly struct ActionQueueMemberObservation
{
    internal ActionQueueMemberObservation(
        long lifecycle,
        ulong publicationGeneration,
        Guid queueId,
        Guid memberId,
        string exactNativeType,
        int memberStacks,
        int authoritativePending,
        int totalStacks,
        int remainingRoom,
        bool observedAfterRestart)
    {
        Lifecycle = lifecycle;
        PublicationGeneration = publicationGeneration;
        QueueId = queueId;
        MemberId = memberId;
        ExactNativeType = exactNativeType ?? string.Empty;
        MemberStacks = memberStacks;
        AuthoritativePending = authoritativePending;
        TotalStacks = totalStacks;
        RemainingRoom = remainingRoom;
        ObservedAfterRestart = observedAfterRestart;
    }

    internal long Lifecycle { get; }
    internal ulong PublicationGeneration { get; }
    internal Guid QueueId { get; }
    internal Guid MemberId { get; }
    internal string ExactNativeType { get; }
    internal int MemberStacks { get; }
    internal int AuthoritativePending { get; }
    internal int TotalStacks { get; }
    internal int RemainingRoom { get; }
    internal bool ObservedAfterRestart { get; }
}

internal enum ActionQueueIntegrityVerdict
{
    InvalidEvidence = 0,
    Clean = 1,
    UnsupportedNativeType = 2,
    AuthoritativePendingExceedsMemberStacks = 3,
    RecoverableExcess = 4,
    PostRestartUpgradeAmbiguous = 5,
}

/// <summary>Pure classification of one queue-member observation.</summary>
internal readonly struct ActionQueueIntegrityFinding
{
    internal ActionQueueIntegrityFinding(
        ActionQueueIntegrityVerdict verdict,
        int excessStacks,
        string reason)
    {
        Verdict = verdict;
        ExcessStacks = excessStacks;
        Reason = reason ?? string.Empty;
    }

    internal ActionQueueIntegrityVerdict Verdict { get; }
    internal int ExcessStacks { get; }
    internal string Reason { get; }
    internal bool CanIssueRecoveryTicket =>
        Verdict == ActionQueueIntegrityVerdict.RecoverableExcess && ExcessStacks > 0;
}

internal static class ActionQueueIntegrityClassifier
{
    internal const string StructureNativeType = "StructureSO";
    internal const string UpgradeNativeType = "UpgradeSO";

    internal static ActionQueueIntegrityFinding Classify(
        in ActionQueueMemberObservation observation)
    {
        if (observation.Lifecycle <= 0 ||
            observation.PublicationGeneration == 0 ||
            observation.QueueId == Guid.Empty ||
            observation.MemberId == Guid.Empty ||
            string.IsNullOrWhiteSpace(observation.ExactNativeType) ||
            observation.MemberStacks < 0 ||
            observation.AuthoritativePending < 0 ||
            observation.TotalStacks < 0 ||
            observation.RemainingRoom < 0 ||
            observation.TotalStacks < observation.MemberStacks)
        {
            return new ActionQueueIntegrityFinding(
                ActionQueueIntegrityVerdict.InvalidEvidence,
                0,
                "The action-queue observation did not contain valid lifecycle, exact identity, " +
                "or non-negative native counts.");
        }

        var isStructure = string.Equals(
            observation.ExactNativeType,
            StructureNativeType,
            StringComparison.Ordinal);
        var isUpgrade = string.Equals(
            observation.ExactNativeType,
            UpgradeNativeType,
            StringComparison.Ordinal);
        if (!isStructure && !isUpgrade)
        {
            return new ActionQueueIntegrityFinding(
                ActionQueueIntegrityVerdict.UnsupportedNativeType,
                0,
                $"Queue member {observation.MemberId:D} has unsupported exact native type " +
                $"'{observation.ExactNativeType}'.");
        }

        if (observation.AuthoritativePending > observation.MemberStacks)
        {
            return new ActionQueueIntegrityFinding(
                ActionQueueIntegrityVerdict.AuthoritativePendingExceedsMemberStacks,
                0,
                $"Queue member {observation.MemberId:D} reports " +
                $"authoritativePending={observation.AuthoritativePending} above " +
                $"memberStacks={observation.MemberStacks}; recovery cannot invent native work.");
        }

        var excess = observation.MemberStacks - observation.AuthoritativePending;
        if (excess == 0)
        {
            return new ActionQueueIntegrityFinding(
                ActionQueueIntegrityVerdict.Clean,
                0,
                $"Queue member {observation.MemberId:D} has matching native stack and " +
                "authoritative pending counts.");
        }

        // Upgrade state restored from a save has an ordering ambiguity that has not been audited:
        // a queued-level record and its stacked action can be reconstructed independently. A
        // numeric excess is useful containment evidence, but is not permission to discard it.
        if (isUpgrade && observation.ObservedAfterRestart)
        {
            return new ActionQueueIntegrityFinding(
                ActionQueueIntegrityVerdict.PostRestartUpgradeAmbiguous,
                excess,
                $"Post-restart UpgradeSO {observation.MemberId:D} has stack excess {excess}, " +
                "but its restored queue ordering is not authoritative enough for recovery.");
        }

        return new ActionQueueIntegrityFinding(
            ActionQueueIntegrityVerdict.RecoverableExcess,
            excess,
            $"{observation.ExactNativeType} {observation.MemberId:D} has exactly {excess} " +
            "stack(s) beyond its authoritative pending count.");
    }
}

/// <summary>
/// Exact member-state fingerprint used both to deduplicate tickets and to enforce one recovery
/// attempt for one observed contradiction.
/// </summary>
internal readonly struct ActionQueueRecoveryFingerprint : IEquatable<ActionQueueRecoveryFingerprint>
{
    internal ActionQueueRecoveryFingerprint(in ActionQueueMemberObservation observation)
    {
        Lifecycle = observation.Lifecycle;
        QueueId = observation.QueueId;
        MemberId = observation.MemberId;
        ExactNativeType = observation.ExactNativeType;
        MemberStacks = observation.MemberStacks;
        AuthoritativePending = observation.AuthoritativePending;
        ExcessStacks = observation.MemberStacks - observation.AuthoritativePending;
        ObservedAfterRestart = observation.ObservedAfterRestart;
    }

    internal long Lifecycle { get; }
    internal Guid QueueId { get; }
    internal Guid MemberId { get; }
    internal string ExactNativeType { get; }
    internal int MemberStacks { get; }
    internal int AuthoritativePending { get; }
    internal int ExcessStacks { get; }
    internal bool ObservedAfterRestart { get; }

    public bool Equals(ActionQueueRecoveryFingerprint other) =>
        Lifecycle == other.Lifecycle &&
        QueueId == other.QueueId &&
        MemberId == other.MemberId &&
        string.Equals(ExactNativeType, other.ExactNativeType, StringComparison.Ordinal) &&
        MemberStacks == other.MemberStacks &&
        AuthoritativePending == other.AuthoritativePending &&
        ExcessStacks == other.ExcessStacks &&
        ObservedAfterRestart == other.ObservedAfterRestart;

    public override bool Equals(object? obj) =>
        obj is ActionQueueRecoveryFingerprint other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Lifecycle.GetHashCode();
            hash = (hash * 397) ^ QueueId.GetHashCode();
            hash = (hash * 397) ^ MemberId.GetHashCode();
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ExactNativeType);
            hash = (hash * 397) ^ MemberStacks;
            hash = (hash * 397) ^ AuthoritativePending;
            hash = (hash * 397) ^ ExcessStacks;
            return (hash * 397) ^ ObservedAfterRestart.GetHashCode();
        }
    }
}

/// <summary>
/// Capability issued only after the same exact contradiction is present in two strictly newer
/// publications of one lifecycle.
/// </summary>
internal readonly struct ActionQueueRecoveryTicket
{
    internal ActionQueueRecoveryTicket(
        Guid ticketId,
        in ActionQueueMemberObservation observation,
        in ActionQueueIntegrityFinding finding)
    {
        TicketId = ticketId;
        Observation = observation;
        Finding = finding;
        Fingerprint = new ActionQueueRecoveryFingerprint(in observation);
    }

    internal Guid TicketId { get; }
    internal ActionQueueMemberObservation Observation { get; }
    internal ActionQueueIntegrityFinding Finding { get; }
    internal ActionQueueRecoveryFingerprint Fingerprint { get; }
    internal bool IsValid => TicketId != Guid.Empty && Finding.CanIssueRecoveryTicket;
}

internal readonly struct ActionQueueIntegrityTrackingResult
{
    internal ActionQueueIntegrityTrackingResult(
        in ActionQueueIntegrityFinding finding,
        int stableObservations,
        ActionQueueRecoveryTicket? ticket)
    {
        Finding = finding;
        StableObservations = stableObservations;
        Ticket = ticket;
    }

    internal ActionQueueIntegrityFinding Finding { get; }
    internal int StableObservations { get; }
    internal ActionQueueRecoveryTicket? Ticket { get; }
}

/// <summary>
/// Bounded lifecycle-scoped evidence tracker. It issues at most one ticket for an exact member
/// fingerprint, and never turns a single collected reading into mutation authority.
/// </summary>
internal sealed class ActionQueueIntegrityTracker
{
    internal const int RequiredStableObservations = 2;
    private const int MaximumTrackedMembers = 512;
    private const int MaximumIssuedFingerprints = 2048;

    private readonly Dictionary<MemberKey, TrackedEvidence> _tracked = new();
    private readonly HashSet<ActionQueueRecoveryFingerprint> _issued = new();
    private long _lifecycle;

    internal ActionQueueIntegrityTrackingResult Observe(
        in ActionQueueMemberObservation observation)
    {
        if (observation.Lifecycle > 0 && observation.Lifecycle != _lifecycle)
        {
            _tracked.Clear();
            _issued.Clear();
            _lifecycle = observation.Lifecycle;
        }

        var finding = ActionQueueIntegrityClassifier.Classify(in observation);
        var key = new MemberKey(
            observation.QueueId,
            observation.MemberId,
            observation.ExactNativeType);
        if (!finding.CanIssueRecoveryTicket)
        {
            _tracked.Remove(key);
            return new ActionQueueIntegrityTrackingResult(in finding, 0, null);
        }

        var fingerprint = new ActionQueueRecoveryFingerprint(in observation);
        var stable = 1;
        if (_tracked.TryGetValue(key, out var prior) &&
            prior.Fingerprint.Equals(fingerprint))
        {
            if (observation.PublicationGeneration <= prior.LastPublicationGeneration)
            {
                return new ActionQueueIntegrityTrackingResult(
                    in finding,
                    prior.StableObservations,
                    null);
            }
            stable = prior.StableObservations + 1;
        }
        else if (_tracked.Count >= MaximumTrackedMembers)
        {
            return new ActionQueueIntegrityTrackingResult(in finding, 0, null);
        }

        _tracked[key] = new TrackedEvidence(
            fingerprint,
            observation.PublicationGeneration,
            stable);

        ActionQueueRecoveryTicket? ticket = null;
        if (stable >= RequiredStableObservations &&
            (_issued.Contains(fingerprint) || _issued.Count < MaximumIssuedFingerprints) &&
            _issued.Add(fingerprint))
        {
            ticket = new ActionQueueRecoveryTicket(
                Guid.NewGuid(),
                in observation,
                in finding);
        }
        return new ActionQueueIntegrityTrackingResult(in finding, stable, ticket);
    }

    internal void InvalidateLifecycle()
    {
        _tracked.Clear();
        _issued.Clear();
        _lifecycle = 0;
    }

    private readonly struct MemberKey : IEquatable<MemberKey>
    {
        internal MemberKey(Guid queueId, Guid memberId, string exactNativeType)
        {
            QueueId = queueId;
            MemberId = memberId;
            ExactNativeType = exactNativeType ?? string.Empty;
        }

        private Guid QueueId { get; }
        private Guid MemberId { get; }
        private string ExactNativeType { get; }

        public bool Equals(MemberKey other) =>
            QueueId == other.QueueId &&
            MemberId == other.MemberId &&
            string.Equals(ExactNativeType, other.ExactNativeType, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is MemberKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = QueueId.GetHashCode();
                hash = (hash * 397) ^ MemberId.GetHashCode();
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ExactNativeType);
            }
        }
    }

    private readonly struct TrackedEvidence
    {
        internal TrackedEvidence(
            ActionQueueRecoveryFingerprint fingerprint,
            ulong lastPublicationGeneration,
            int stableObservations)
        {
            Fingerprint = fingerprint;
            LastPublicationGeneration = lastPublicationGeneration;
            StableObservations = stableObservations;
        }

        internal ActionQueueRecoveryFingerprint Fingerprint { get; }
        internal ulong LastPublicationGeneration { get; }
        internal int StableObservations { get; }
    }
}
