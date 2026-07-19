using System;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

internal readonly struct AutomationAdmissionIdentity
{
    public AutomationAdmissionIdentity(string stableId, string expectedNativeType)
    {
        StableId = stableId;
        ExpectedNativeType = expectedNativeType;
    }

    public string StableId { get; }

    public string ExpectedNativeType { get; }

    public bool IsKnown =>
        !string.IsNullOrWhiteSpace(StableId) &&
        !string.IsNullOrWhiteSpace(ExpectedNativeType);
}

internal readonly struct AutomationAdmissionSnapshot
{
    public AutomationAdmissionSnapshot(
        AutomationActionFamily family,
        AutomationAdmissionIdentity identity,
        bool availabilityKnown,
        bool isAvailable,
        string availabilityReason,
        bool nativeAdmissionKnown,
        bool nativeAdmissionAccepted,
        string nativeAdmissionReason,
        bool immediateCostsKnown,
        IReadOnlyList<ResourceAdmissionCost> immediateCosts,
        bool drainCostsKnown,
        IReadOnlyList<ResourceAdmissionCost> drainCosts,
        bool queueRequirementKnown,
        int requiredQueueSlots)
    {
        Family = family;
        Identity = identity;
        AvailabilityKnown = availabilityKnown;
        IsAvailable = isAvailable;
        AvailabilityReason = availabilityReason;
        NativeAdmissionKnown = nativeAdmissionKnown;
        NativeAdmissionAccepted = nativeAdmissionAccepted;
        NativeAdmissionReason = nativeAdmissionReason;
        ImmediateCostsKnown = immediateCostsKnown;
        ImmediateCosts = immediateCosts;
        DrainCostsKnown = drainCostsKnown;
        DrainCosts = drainCosts;
        QueueRequirementKnown = queueRequirementKnown;
        RequiredQueueSlots = requiredQueueSlots;
    }

    public AutomationActionFamily Family { get; }

    public AutomationAdmissionIdentity Identity { get; }

    public bool AvailabilityKnown { get; }

    public bool IsAvailable { get; }

    public string AvailabilityReason { get; }

    public bool NativeAdmissionKnown { get; }

    public bool NativeAdmissionAccepted { get; }

    public string NativeAdmissionReason { get; }

    public bool ImmediateCostsKnown { get; }

    public IReadOnlyList<ResourceAdmissionCost> ImmediateCosts { get; }

    public bool DrainCostsKnown { get; }

    public IReadOnlyList<ResourceAdmissionCost> DrainCosts { get; }

    public bool QueueRequirementKnown { get; }

    public int RequiredQueueSlots { get; }
}

internal static class AutomationAdmissionPolicy
{
    public static bool HasCompleteContract(AutomationAdmissionSnapshot snapshot, out string reason)
    {
        if (!snapshot.Identity.IsKnown)
        {
            reason = "stable identity or expected native type is unknown";
            return false;
        }

        if (!snapshot.AvailabilityKnown)
        {
            reason = "native availability is unknown";
            return false;
        }

        if (!snapshot.NativeAdmissionKnown)
        {
            reason = "native admission is unknown";
            return false;
        }

        if (!snapshot.ImmediateCostsKnown || snapshot.ImmediateCosts is null)
        {
            reason = "immediate native costs are unknown";
            return false;
        }

        if (!snapshot.DrainCostsKnown || snapshot.DrainCosts is null)
        {
            reason = "native drain costs are unknown";
            return false;
        }

        if (!snapshot.QueueRequirementKnown || snapshot.RequiredQueueSlots < 0)
        {
            reason = "native queue requirement is unknown";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

internal static class AutoBuyAdmissionAdapter
{
    private static readonly IReadOnlyList<ResourceAdmissionCost> NoDrains = Array.Empty<ResourceAdmissionCost>();

    public static AutomationAdmissionSnapshot Capture(IAutoBuyCandidate candidate)
    {
        var candidateSnapshot = candidate.Snapshot();
        var nativeAdmissionAccepted = false;
        var nativeReason = string.Empty;
        bool available;
        bool availabilityKnown;
        if (candidateSnapshot.Kind == AutoBuyCandidateKind.Upgrade)
        {
            nativeAdmissionAccepted = candidate.CanPurchase(out nativeReason);
            availabilityKnown = TryReadAvailability(candidate, out available);
        }
        else
        {
            availabilityKnown = TryReadAvailability(candidate, out available);
            if (availabilityKnown && available)
            {
                nativeAdmissionAccepted = candidate.CanPurchase(out nativeReason);
            }
        }

        IReadOnlyList<ResourceAdmissionCost> costs = Array.Empty<ResourceAdmissionCost>();
        var costsKnown = true;
        if (available)
        {
            costs = candidate.GetCosts();
            costsKnown = candidate is not IAutoBuyDirtyCandidate dirty || dirty.HasResolvedCosts;
        }

        return new AutomationAdmissionSnapshot(
            candidateSnapshot.Kind == AutoBuyCandidateKind.Structure
                ? AutomationActionFamily.StructurePurchase
                : AutomationActionFamily.UpgradePurchase,
            new AutomationAdmissionIdentity(candidateSnapshot.Uuid, candidateSnapshot.ReflectedType),
            availabilityKnown,
            isAvailable: available,
            availabilityReason: available
                ? string.Empty
                : candidateSnapshot.Kind == AutoBuyCandidateKind.Structure ? "structure is locked" : "upgrade is not available",
            nativeAdmissionKnown: (candidate is not IAutoBuyAdmissionContractEvidence contract ||
                                   contract.HasCompleteNativeContract) &&
                                  (!available ||
                                   !string.Equals(nativeReason, "CanPurchase unavailable", StringComparison.Ordinal)),
            nativeAdmissionAccepted,
            nativeReason,
            immediateCostsKnown: costsKnown,
            immediateCosts: costs,
            drainCostsKnown: true,
            drainCosts: NoDrains,
            queueRequirementKnown: true,
            requiredQueueSlots: 1);
    }

    private static bool TryReadAvailability(IAutoBuyCandidate candidate, out bool available)
    {
        if (candidate is IAutoBuyAvailabilityEvidence evidence)
        {
            return evidence.TryReadAvailability(out available);
        }

        available = candidate.IsAvailable();
        return true;
    }
}

internal static class AutoCastAdmissionAdapter
{
    public static AutomationAdmissionSnapshot Capture(IAutoCastCandidate candidate)
    {
        var identityKnown = candidate.TryGetIdentity(out var identity, out var identityReason);
        if (!identityKnown)
        {
            return new AutomationAdmissionSnapshot(
                AutomationActionFamily.SpellCast,
                new AutomationAdmissionIdentity(string.Empty, "Spell"),
                availabilityKnown: false,
                isAvailable: false,
                availabilityReason: identityReason,
                nativeAdmissionKnown: false,
                nativeAdmissionAccepted: false,
                nativeAdmissionReason: identityReason,
                immediateCostsKnown: false,
                immediateCosts: Array.Empty<ResourceAdmissionCost>(),
                drainCostsKnown: false,
                drainCosts: Array.Empty<ResourceAdmissionCost>(),
                queueRequirementKnown: true,
                requiredQueueSlots: 0);
        }

        var isAvailable = !candidate.IsEmpty && !candidate.IsCasting;
        var availabilityReason = candidate.IsEmpty
            ? "empty slot"
            : candidate.IsCasting
                ? candidate.Kind == AutoCastSpellKind.Aura ? "aura already active" : "already casting"
                : string.Empty;
        var nativeAccepted = false;
        var nativeReason = availabilityReason;
        var immediateKnown = true;
        IReadOnlyList<ResourceAdmissionCost> immediateCosts = Array.Empty<ResourceAdmissionCost>();
        var drainsKnown = true;
        IReadOnlyList<ResourceAdmissionCost> drainCosts = Array.Empty<ResourceAdmissionCost>();
        if (isAvailable)
        {
            nativeAccepted = candidate.CanCast(out nativeReason);
            if (nativeAccepted)
            {
                immediateKnown = candidate.TryGetImmediateCosts(out immediateCosts);
                drainsKnown = immediateKnown && candidate.TryGetDrainCosts(out drainCosts);
            }
        }

        var evidence = candidate as IAutoCastAdmissionFailureEvidence;
        var reasonEvidence = candidate as IAutoCastAdmissionFailureReasonEvidence;
        var contractKnown = evidence?.LastAdmissionFailure != AutoCastAdmissionFailureKind.ContractUnavailable;
        if (!contractKnown && !string.IsNullOrWhiteSpace(reasonEvidence?.LastAdmissionFailureReason))
        {
            nativeReason = reasonEvidence.LastAdmissionFailureReason;
        }

        return new AutomationAdmissionSnapshot(
            AutomationActionFamily.SpellCast,
            new AutomationAdmissionIdentity(
                identityKnown ? identity.Uuid : string.Empty,
                identityKnown ? identity.NativeType.FullName ?? identity.NativeType.Name : identityReason),
            availabilityKnown: true,
            isAvailable,
            availabilityReason,
            nativeAdmissionKnown: contractKnown,
            nativeAdmissionAccepted: nativeAccepted,
            nativeAdmissionReason: nativeReason,
            immediateCostsKnown: immediateKnown,
            immediateCosts,
            drainCostsKnown: drainsKnown,
            drainCosts,
            queueRequirementKnown: true,
            requiredQueueSlots: 0);
    }

    public static bool TryValidateTargets(
        IAutoCastCandidate candidate,
        out string reason,
        out AutoCastAdmissionFailureKind failureKind)
    {
        if (candidate.HasValidTargets(out reason))
        {
            failureKind = AutoCastAdmissionFailureKind.None;
            return true;
        }

        failureKind = candidate is IAutoCastAdmissionFailureEvidence evidence
            ? evidence.LastAdmissionFailure
            : AutoCastAdmissionFailureKind.OrdinaryRejection;
        if (candidate is IAutoCastAdmissionFailureReasonEvidence reasonEvidence &&
            !string.IsNullOrWhiteSpace(reasonEvidence.LastAdmissionFailureReason))
        {
            reason = reasonEvidence.LastAdmissionFailureReason;
        }
        return false;
    }
}
