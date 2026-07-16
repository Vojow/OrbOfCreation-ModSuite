using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal enum AutoBuyCandidateKind
{
    Structure,
    Upgrade
}

internal enum AutoBuyCandidateLifecycleState
{
    Registered,
    Locked,
    Available,
    Queued,
    TerminalQueued,
    Completed,
    Invalid,
}

[Flags]
internal enum AutoBuyDirtyReason
{
    None = 0,
    IdentityDirty = 1 << 0,
    AvailabilityDirty = 1 << 1,
    LevelDirty = 1 << 2,
    CostDirty = 1 << 3,
    ResourceDirty = 1 << 4,
    PriorityDirty = 1 << 5,
    CompletionDirty = 1 << 6,
    LifecycleDirty = IdentityDirty | AvailabilityDirty | LevelDirty | CompletionDirty,
    EvaluationDirty = CostDirty | ResourceDirty | PriorityDirty,
    All = LifecycleDirty | EvaluationDirty,
}

[Flags]
internal enum AutoBuyResourceChange
{
    None = 0,
    Quantity = 1 << 0,
    Capacity = 1 << 1,
    Quality = 1 << 2,
    AttributeCost = 1 << 3,
    Availability = 1 << 4,
    Identity = 1 << 5,
    Unknown = 1 << 6,
}

internal readonly struct AutoBuyLifecycleEvidence
{
    public AutoBuyLifecycleEvidence(
        bool isAvailable,
        int currentLevel,
        int queuedLevels,
        bool hasFiniteLevels,
        bool isMaxLevel,
        bool isMaxQueuedLevel)
    {
        IsAvailable = isAvailable;
        CurrentLevel = currentLevel;
        QueuedLevels = queuedLevels;
        HasFiniteLevels = hasFiniteLevels;
        IsMaxLevel = isMaxLevel;
        IsMaxQueuedLevel = isMaxQueuedLevel;
    }

    public bool IsAvailable { get; }

    public int CurrentLevel { get; }

    public int QueuedLevels { get; }

    public bool HasFiniteLevels { get; }

    public bool IsMaxLevel { get; }

    public bool IsMaxQueuedLevel { get; }
}

internal interface IAutoBuyCatalog : IDisposable
{
    IEnumerable<IAutoBuyCandidate> Discover();

    bool TryGetRemainingQueueRoom(out int remainingRoom);

    bool TryGetBulkDevelopment(out int levels);

    bool TryGetActionMultiplier(out int multiplier);
}

internal interface IAutoBuyIncrementalCatalog
{
    AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request);

    void CompleteCandidateEvaluation(IAutoBuyCandidate candidate, bool policyExcluded);

    void InvalidatePolicy();

    void BeginMutationEvaluation();

    void NotifyPurchaseAttempted(IAutoBuyCandidate candidate);

    void NotifyStructureQueueChanged();

    void NotifyUpgradeQueueChanged();

    void NotifyNativeCompletion();

    void InvalidateLifecycle();
}

internal readonly struct AutoBuyEvaluationRequest
{
    public AutoBuyEvaluationRequest(int candidateLimit, bool includeStructures, bool includeUpgrades)
    {
        CandidateLimit = candidateLimit;
        IncludeStructures = includeStructures;
        IncludeUpgrades = includeUpgrades;
    }

    public int CandidateLimit { get; }

    public bool IncludeStructures { get; }

    public bool IncludeUpgrades { get; }
}

internal sealed class AutoBuyEvaluationBatch
{
    public AutoBuyEvaluationBatch(
        IReadOnlyList<IAutoBuyCandidate> activeCandidates,
        IReadOnlyList<IAutoBuyCandidate> dirtyCandidates,
        IAutoBuyCandidate? firstExcludedCandidate,
        bool reconciliationPending)
    {
        ActiveCandidates = activeCandidates;
        DirtyCandidates = dirtyCandidates;
        FirstExcludedCandidate = firstExcludedCandidate;
        ReconciliationPending = reconciliationPending;
    }

    public IReadOnlyList<IAutoBuyCandidate> ActiveCandidates { get; }

    public IReadOnlyList<IAutoBuyCandidate> DirtyCandidates { get; }

    public IAutoBuyCandidate? FirstExcludedCandidate { get; }

    public bool ReconciliationPending { get; }
}

internal interface IAutoBuyCandidate
{
    AutoBuyCandidateSnapshot Snapshot();

    bool IsAvailable();

    bool CanPurchase(out string reason);

    IReadOnlyList<ResourceAdmissionCost> GetCosts();

    bool TryPurchaseOne(out string reason);
}

internal interface IAutoBuyNativeIdentity
{
    object NativeIdentity { get; }
}

internal interface IAutoBuyLifecycleCandidate
{
    bool TryGetLifecycleEvidence(out AutoBuyLifecycleEvidence evidence, out string reason);
}

internal interface IAutoBuyDirtyCandidate
{
    IReadOnlyList<string> ResourceDependencies { get; }

    bool HasResolvedCosts { get; }

    void MarkDirty(AutoBuyDirtyReason reasons);

    void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence);
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
