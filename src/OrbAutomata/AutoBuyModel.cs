using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

internal enum AutoBuyCandidateKind
{
    Structure,
    Upgrade
}

[Flags]
internal enum AutoBuyCandidateKinds
{
    None = 0,
    Structures = 1 << 0,
    Upgrades = 1 << 1,
    All = Structures | Upgrades,
}

[Flags]
internal enum AutoBuyEconomicPriority
{
    None = 0,
    CostReduction = 1 << 0,
    QualityIncrease = 1 << 1,
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

    bool TryCaptureQueueCapacity(
        int automationUsageLimit,
        int manualReservation,
        out QueueCapacitySnapshot snapshot);

    bool TryGetBulkDevelopment(out int levels);

    bool TryGetActionMultiplier(out int multiplier);
}

internal interface IAutoBuyIncrementalCatalog
{
    AutoBuyEvaluationBatch BeginEvaluation(AutoBuyEvaluationRequest request);

    void CompleteCandidateEvaluation(
        IAutoBuyCandidate candidate,
        bool suppressResourceTracking,
        bool policyExcluded,
        AutoBuyDecision? decision = null);

    void InvalidatePolicy();

    void BeginMutationEvaluation();

    void NotifyPurchaseAttempted(IAutoBuyCandidate candidate);

    void CompleteMutationGroup();

    void NotifyStructureQueueChanged(object nativeIdentity);

    void NotifyUpgradeQueueChanged(object nativeIdentity);

    void NotifyNativeCompletion();

    void InvalidateLifecycle();
}

internal interface IAutoBuyProgressionCatalog
{
    void NotifyNativeCompletion(object nativeIdentity, AutoBuyCandidateKind completedKind);
}

internal interface IAutoBuyInvalidationIdentityCatalog
{
    bool TryResolveInvalidationTarget(
        object nativeIdentity,
        AutoBuyCandidateKind expectedKind,
        out string entityId,
        out string expectedTypeName);
}

internal interface IAutoBuyCompletionRevalidationCatalog
{
    bool TryRefreshCandidateAfterCompletion(
        IAutoBuyCandidate candidate,
        long completionGeneration,
        out string reason);
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

internal interface IAutoBuyMutationCandidate
{
    void RecoverMutationBlock();
}

internal interface IAutoBuyDirtyCandidate
{
    IReadOnlyList<string> ResourceDependencies { get; }

    bool HasResolvedCosts { get; }

    void MarkDirty(AutoBuyDirtyReason reasons);

    void SetLifecycleEvidence(AutoBuyLifecycleEvidence evidence);
}

internal interface IAutoBuyPriorityCandidate
{
    AutoBuyEconomicPriority EconomicPriority { get; }
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

internal enum AutoBuyRejectionReason
{
    None,
    MutationQuarantined,
    NotAllowed,
    ConfigurationBlocked,
    NativeNotPurchasable,
    Unavailable,
    Locked,
    CostSnapshotUnavailable,
    InvalidReservePolicy,
    InvalidResourceSnapshot,
    ReserveViolation,
    AffordabilityThreshold,
    CandidateScanLimit,
}

internal enum AutoBuyResourceBlockerKind
{
    ReserveFloor,
    AffordabilityThreshold,
}

internal readonly struct AutoBuyResourceBlocker
{
    public AutoBuyResourceBlocker(
        AutoBuyResourceBlockerKind kind,
        string resourceId,
        string resourceName,
        BigAmount cost,
        BigAmount availableQuantity,
        BigAmount requiredQuantity,
        bool isBandwidth = false)
    {
        Kind = kind;
        ResourceId = resourceId;
        ResourceName = resourceName;
        Cost = cost;
        AvailableQuantity = availableQuantity;
        RequiredQuantity = requiredQuantity;
        IsBandwidth = isBandwidth;
    }

    public AutoBuyResourceBlockerKind Kind { get; }

    public string ResourceId { get; }

    public string ResourceName { get; }

    public BigAmount Cost { get; }

    public BigAmount AvailableQuantity { get; }

    public BigAmount RequiredQuantity { get; }

    public bool IsBandwidth { get; }
}

internal sealed class AutoBuyDecision
{
    private AutoBuyDecision(
        AutoBuyDecisionKind kind,
        AutoBuyCandidateSnapshot candidate,
        double costRatio,
        int priorityRank,
        string detail,
        AutoBuyRejectionReason rejectionReason,
        IReadOnlyList<AutoBuyResourceBlocker> resourceBlockers)
    {
        Kind = kind;
        Candidate = candidate;
        CostRatio = costRatio;
        PriorityRank = priorityRank;
        Detail = detail;
        RejectionReason = rejectionReason;
        ResourceBlockers = resourceBlockers;
    }

    public AutoBuyDecisionKind Kind { get; }

    public AutoBuyCandidateSnapshot Candidate { get; }

    public double CostRatio { get; }

    public int PriorityRank { get; }

    public string Detail { get; }

    public AutoBuyRejectionReason RejectionReason { get; }

    public IReadOnlyList<AutoBuyResourceBlocker> ResourceBlockers { get; }

    public static AutoBuyDecision Recommended(
        AutoBuyCandidateSnapshot candidate,
        double costRatio,
        string detail,
        int priorityRank = 0)
    {
        return new AutoBuyDecision(
            AutoBuyDecisionKind.Recommendation,
            candidate,
            costRatio,
            priorityRank,
            detail,
            AutoBuyRejectionReason.None,
            Array.Empty<AutoBuyResourceBlocker>());
    }

    public static AutoBuyDecision Rejected(
        AutoBuyCandidateSnapshot candidate,
        AutoBuyRejectionReason rejectionReason,
        string detail,
        IReadOnlyList<AutoBuyResourceBlocker>? resourceBlockers = null)
    {
        if (rejectionReason == AutoBuyRejectionReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }

        return new AutoBuyDecision(
            AutoBuyDecisionKind.Rejection,
            candidate,
            double.PositiveInfinity,
            0,
            detail,
            rejectionReason,
            resourceBlockers ?? Array.Empty<AutoBuyResourceBlocker>());
    }
}
