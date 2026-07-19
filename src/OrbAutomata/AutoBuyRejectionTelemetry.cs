using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoBuyRejectionTelemetry
{
    private readonly Dictionary<AutomationDecisionCode, long> _rejectionsByCode =
        new Dictionary<AutomationDecisionCode, long>();
    private readonly Dictionary<string, AutomationDecisionConditionKey> _latestRejections =
        new Dictionary<string, AutomationDecisionConditionKey>(StringComparer.OrdinalIgnoreCase);

    public long Evaluations { get; private set; }

    public long Recommendations { get; private set; }

    public long Rejections { get; private set; }

    public long RepeatedUnchangedRejections { get; private set; }

    public long RejectionStateChanges { get; private set; }

    public long RejectionExits { get; private set; }

    public long ScanLimitDeferrals { get; private set; }

    public int CurrentRejectedCandidates => _latestRejections.Count;

    public bool Record(AutoBuyDecision decision)
    {
        if (decision.Code == AutomationDecisionCode.ScanLimitDeferred)
        {
            ScanLimitDeferrals++;
            return false;
        }

        Evaluations++;
        var uuid = decision.Candidate.Uuid;
        if (decision.Kind == AutoBuyDecisionKind.Recommendation)
        {
            Recommendations++;
            if (_latestRejections.Remove(uuid))
            {
                RejectionExits++;
            }
            return false;
        }

        Rejections++;
        _rejectionsByCode.TryGetValue(decision.Code, out var reasonCount);
        _rejectionsByCode[decision.Code] = reasonCount + 1;

        var conditionKey = decision.StructuredDecision.ConditionKey;
        if (_latestRejections.TryGetValue(uuid, out var previous) && previous.Equals(conditionKey))
        {
            RepeatedUnchangedRejections++;
            return false;
        }

        RejectionStateChanges++;
        _latestRejections[uuid] = conditionKey;
        return true;
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
            RejectionExits,
            ScanLimitDeferrals,
            CurrentRejectedCandidates,
            new Dictionary<AutomationDecisionCode, long>(_rejectionsByCode));
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
        long rejectionExits,
        long scanLimitDeferrals,
        int currentRejectedCandidates,
        IReadOnlyDictionary<AutomationDecisionCode, long> rejectionsByCode)
    {
        Evaluations = evaluations;
        Recommendations = recommendations;
        Rejections = rejections;
        RepeatedUnchangedRejections = repeatedUnchangedRejections;
        RejectionStateChanges = rejectionStateChanges;
        RejectionExits = rejectionExits;
        ScanLimitDeferrals = scanLimitDeferrals;
        CurrentRejectedCandidates = currentRejectedCandidates;
        RejectionsByCode = rejectionsByCode;
    }

    public long Evaluations { get; }

    public long Recommendations { get; }

    public long Rejections { get; }

    public long RepeatedUnchangedRejections { get; }

    public long RejectionStateChanges { get; }

    public long RejectionExits { get; }

    public long ScanLimitDeferrals { get; }

    public int CurrentRejectedCandidates { get; }

    public IReadOnlyDictionary<AutomationDecisionCode, long> RejectionsByCode { get; }

    public string FormatCodeCounts()
    {
        return RejectionsByCode.Count == 0
            ? "none"
            : string.Join(
                ",",
                RejectionsByCode
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
