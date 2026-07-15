using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoBuyCandidateKind
{
    Structure,
    Upgrade
}

internal interface IAutoBuyCatalog : IDisposable
{
    IEnumerable<IAutoBuyCandidate> Discover();

    bool TryGetRemainingQueueRoom(out int remainingRoom);

    bool TryGetBulkDevelopment(out int levels);

    bool TryGetActionMultiplier(out int multiplier);
}

internal interface IAutoBuyCandidate
{
    AutoBuyCandidateSnapshot Snapshot();

    bool IsAvailable();

    bool CanPurchase(out string reason);

    IReadOnlyList<ResourceAdmissionCost> GetCosts();

    bool TryPurchaseOne(out string reason);
}

internal sealed class AutoBuyCandidateSnapshot
{
    public AutoBuyCandidateSnapshot(
        IAutoBuyCandidate source,
        string uuid,
        string displayName,
        AutoBuyCandidateKind kind,
        string reflectedType)
    {
        Source = source;
        Uuid = uuid;
        DisplayName = displayName;
        Kind = kind;
        ReflectedType = reflectedType;
    }

    public IAutoBuyCandidate Source { get; }

    public string Uuid { get; }

    public string DisplayName { get; }

    public AutoBuyCandidateKind Kind { get; }

    public string ReflectedType { get; }
}

internal enum AutoBuyDecisionKind
{
    Recommendation,
    Rejection
}

internal sealed class AutoBuyDecision
{
    private AutoBuyDecision(
        AutoBuyDecisionKind kind,
        AutoBuyCandidateSnapshot candidate,
        double costRatio,
        string detail)
    {
        Kind = kind;
        Candidate = candidate;
        CostRatio = costRatio;
        Detail = detail;
    }

    public AutoBuyDecisionKind Kind { get; }

    public AutoBuyCandidateSnapshot Candidate { get; }

    public double CostRatio { get; }

    public string Detail { get; }

    public static AutoBuyDecision Recommended(AutoBuyCandidateSnapshot candidate, double costRatio, string detail)
    {
        return new AutoBuyDecision(AutoBuyDecisionKind.Recommendation, candidate, costRatio, detail);
    }

    public static AutoBuyDecision Rejected(AutoBuyCandidateSnapshot candidate, string detail)
    {
        return new AutoBuyDecision(AutoBuyDecisionKind.Rejection, candidate, double.PositiveInfinity, detail);
    }
}
