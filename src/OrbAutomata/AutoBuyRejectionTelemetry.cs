using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata;

internal sealed class AutoBuyRejectionTelemetry
{
    private readonly Dictionary<AutoBuyRejectionReason, long> _rejectionsByReason =
        new Dictionary<AutoBuyRejectionReason, long>();
    private readonly Dictionary<string, AutoBuyRejectionState> _latestRejections =
        new Dictionary<string, AutoBuyRejectionState>(StringComparer.OrdinalIgnoreCase);

    public long Evaluations { get; private set; }

    public long Recommendations { get; private set; }

    public long Rejections { get; private set; }

    public long RepeatedUnchangedRejections { get; private set; }

    public long RejectionStateChanges { get; private set; }

    public int CurrentRejectedCandidates => _latestRejections.Count;

    public void Record(AutoBuyDecision decision)
    {
        Evaluations++;
        var uuid = decision.Candidate.Uuid;
        if (decision.Kind == AutoBuyDecisionKind.Recommendation)
        {
            Recommendations++;
            _latestRejections.Remove(uuid);
            return;
        }

        Rejections++;
        _rejectionsByReason.TryGetValue(decision.RejectionReason, out var reasonCount);
        _rejectionsByReason[decision.RejectionReason] = reasonCount + 1;

        if (decision.RejectionReason == AutoBuyRejectionReason.CandidateScanLimit)
        {
            return;
        }

        if (_latestRejections.TryGetValue(uuid, out var previous) && previous.HasSameBlockingCondition(decision))
        {
            RepeatedUnchangedRejections++;
        }
        else
        {
            RejectionStateChanges++;
            _latestRejections[uuid] = AutoBuyRejectionState.FromDecision(decision);
        }
    }

    public void Remove(string uuid)
    {
        _latestRejections.Remove(uuid);
    }

    public void ClearCurrentStates()
    {
        _latestRejections.Clear();
    }

    public AutoBuyRejectionTelemetrySnapshot Snapshot()
    {
        return new AutoBuyRejectionTelemetrySnapshot(
            Evaluations,
            Recommendations,
            Rejections,
            RepeatedUnchangedRejections,
            RejectionStateChanges,
            CurrentRejectedCandidates,
            new Dictionary<AutoBuyRejectionReason, long>(_rejectionsByReason));
    }

    private sealed class AutoBuyRejectionState
    {
        private AutoBuyRejectionState(
            AutoBuyRejectionReason reason,
            string nonResourceDetail,
            IReadOnlyList<AutoBuyResourceBlocker> resourceBlockers)
        {
            Reason = reason;
            NonResourceDetail = nonResourceDetail;
            ResourceBlockers = resourceBlockers;
        }

        private AutoBuyRejectionReason Reason { get; }

        private string NonResourceDetail { get; }

        private IReadOnlyList<AutoBuyResourceBlocker> ResourceBlockers { get; }

        public static AutoBuyRejectionState FromDecision(AutoBuyDecision decision)
        {
            return new AutoBuyRejectionState(
                decision.RejectionReason,
                decision.ResourceBlockers.Count == 0 ? decision.Detail : string.Empty,
                decision.ResourceBlockers);
        }

        public bool HasSameBlockingCondition(AutoBuyDecision decision)
        {
            var nonResourceDetail = decision.ResourceBlockers.Count == 0 ? decision.Detail : string.Empty;
            if (Reason != decision.RejectionReason ||
                !string.Equals(NonResourceDetail, nonResourceDetail, StringComparison.Ordinal) ||
                ResourceBlockers.Count != decision.ResourceBlockers.Count)
            {
                return false;
            }

            for (var i = 0; i < ResourceBlockers.Count; i++)
            {
                var left = ResourceBlockers[i];
                var right = decision.ResourceBlockers[i];
                if (left.Kind != right.Kind ||
                    !string.Equals(left.ResourceId, right.ResourceId, StringComparison.OrdinalIgnoreCase) ||
                    left.Cost.CompareTo(right.Cost) != 0 ||
                    left.RequiredQuantity.CompareTo(right.RequiredQuantity) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

internal sealed class AutoBuyRejectionTelemetrySnapshot
{
    public AutoBuyRejectionTelemetrySnapshot(
        long evaluations,
        long recommendations,
        long rejections,
        long repeatedUnchangedRejections,
        long rejectionStateChanges,
        int currentRejectedCandidates,
        IReadOnlyDictionary<AutoBuyRejectionReason, long> rejectionsByReason)
    {
        Evaluations = evaluations;
        Recommendations = recommendations;
        Rejections = rejections;
        RepeatedUnchangedRejections = repeatedUnchangedRejections;
        RejectionStateChanges = rejectionStateChanges;
        CurrentRejectedCandidates = currentRejectedCandidates;
        RejectionsByReason = rejectionsByReason;
    }

    public long Evaluations { get; }

    public long Recommendations { get; }

    public long Rejections { get; }

    public long RepeatedUnchangedRejections { get; }

    public long RejectionStateChanges { get; }

    public int CurrentRejectedCandidates { get; }

    public IReadOnlyDictionary<AutoBuyRejectionReason, long> RejectionsByReason { get; }

    public string FormatReasonCounts()
    {
        return RejectionsByReason.Count == 0
            ? "none"
            : string.Join(
                ",",
                RejectionsByReason
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
