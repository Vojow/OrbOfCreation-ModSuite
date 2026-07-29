using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>Why a complete native-free consumable profile could not be built.</summary>
internal enum AutoItemsConsumableProfileStatus
{
    Ready = 0,
    ConsumableMissing = 1,
    NoSupportedFamily = 2,
    AmbiguousSupportedFamily = 3,
    ToxicityResourceMissing = 4,
    ToxicityResourceNotInverted = 5,
    ToxicityCapacityUnavailable = 6,
    ToxicityCostMissing = 7,
    CostAmountInvalid = 8,
}

/// <summary>
/// The fail-closed facts policy needs about one consumable. This is a projection over immutable
/// world rows; it retains non-toxicity costs as explicit flags rather than treating toxicity as the
/// whole native admission rule.
/// </summary>
internal readonly struct AutoItemsConsumableProfile
{
    internal AutoItemsConsumableProfile(
        AutoItemsConsumableProfileStatus status,
        AutoItemsConsumableFamily family,
        in WorldConsumable consumable,
        in WorldResource toxicity,
        BigDouble immediateToxicityCost,
        BigDouble heldToxicityCost,
        bool hasHeldToxicityCost,
        bool hasAdditionalImmediateCosts,
        bool hasAdditionalHeldCosts)
    {
        Status = status;
        Family = family;
        Consumable = consumable;
        Toxicity = toxicity;
        ImmediateToxicityCost = immediateToxicityCost;
        HeldToxicityCost = heldToxicityCost;
        HasHeldToxicityCost = hasHeldToxicityCost;
        HasAdditionalImmediateCosts = hasAdditionalImmediateCosts;
        HasAdditionalHeldCosts = hasAdditionalHeldCosts;
    }

    internal AutoItemsConsumableProfileStatus Status { get; }
    internal bool IsReady => Status == AutoItemsConsumableProfileStatus.Ready;
    internal AutoItemsConsumableFamily Family { get; }
    internal WorldConsumable Consumable { get; }
    internal WorldResource Toxicity { get; }

    /// <summary>The toxicity charged once by <c>consumeCost</c>.</summary>
    internal BigDouble ImmediateToxicityCost { get; }

    /// <summary>Any toxicity held by <c>usageCost</c> while a timed effect is active.</summary>
    internal BigDouble HeldToxicityCost { get; }
    internal bool HasHeldToxicityCost { get; }

    /// <summary>
    /// Whether the corresponding vector also names another resource. Native <c>CanFire()</c> remains
    /// authoritative for those costs; policy may never admit an item from toxicity alone.
    /// </summary>
    internal bool HasAdditionalImmediateCosts { get; }
    internal bool HasAdditionalHeldCosts { get; }
}

internal static class AutoItemsConsumableProfileBuilder
{
    internal static AutoItemsConsumableProfile Build(GameWorldState world, Guid consumableId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!WorldLookup.TryFind(world.Consumables, consumableId, out var consumable))
            return Failure(AutoItemsConsumableProfileStatus.ConsumableMissing);

        var family = Classify(world.ConsumableTypes, consumableId, out var classificationStatus);
        if (classificationStatus != AutoItemsConsumableProfileStatus.Ready)
            return Failure(classificationStatus, family, in consumable);

        if (!WorldLookup.TryFind(world.Resources, KnownEntities.PotionToxicity.Uuid, out var toxicity))
            return Failure(
                AutoItemsConsumableProfileStatus.ToxicityResourceMissing,
                family,
                in consumable);
        if (!toxicity.Reading.Traits.InvertedResource)
            return Failure(
                AutoItemsConsumableProfileStatus.ToxicityResourceNotInverted,
                family,
                in consumable,
                in toxicity);
        if (!toxicity.IsCapped)
            return Failure(
                AutoItemsConsumableProfileStatus.ToxicityCapacityUnavailable,
                family,
                in consumable,
                in toxicity);

        var immediate = SumCosts(
            world.ConsumableCosts,
            consumableId,
            WorldConsumableCostKind.Consume);
        if (!immediate.IsValid)
            return Failure(
                AutoItemsConsumableProfileStatus.CostAmountInvalid,
                family,
                in consumable,
                in toxicity);
        if (!immediate.HasToxicity)
            return Failure(
                AutoItemsConsumableProfileStatus.ToxicityCostMissing,
                family,
                in consumable,
                in toxicity);

        var held = SumCosts(
            world.ConsumableCosts,
            consumableId,
            WorldConsumableCostKind.Usage);
        if (!held.IsValid)
            return Failure(
                AutoItemsConsumableProfileStatus.CostAmountInvalid,
                family,
                in consumable,
                in toxicity);

        return new AutoItemsConsumableProfile(
            AutoItemsConsumableProfileStatus.Ready,
            family,
            in consumable,
            in toxicity,
            immediate.Toxicity,
            held.Toxicity,
            held.HasToxicity,
            immediate.HasOtherResource,
            held.HasOtherResource);
    }

    private static AutoItemsConsumableFamily Classify(
        PublicationTable<WorldConsumableType> types,
        Guid consumableId,
        out AutoItemsConsumableProfileStatus status)
    {
        if (!WorldConsumableTypeLookup.TryFindRange(types, consumableId, out var start, out var count))
        {
            status = AutoItemsConsumableProfileStatus.NoSupportedFamily;
            return AutoItemsConsumableFamily.Unknown;
        }

        var family = AutoItemsConsumableFamily.Unknown;
        var supportedCount = 0;
        for (var index = 0; index < count; index++)
        {
            var candidate = AutoItemsConsumableFamilies.FromTypeId(types[start + index].TypeId);
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            family = candidate;
            supportedCount++;
        }

        if (supportedCount == 0)
        {
            status = AutoItemsConsumableProfileStatus.NoSupportedFamily;
            return AutoItemsConsumableFamily.Unknown;
        }

        if (supportedCount != 1)
        {
            status = AutoItemsConsumableProfileStatus.AmbiguousSupportedFamily;
            return AutoItemsConsumableFamily.Unknown;
        }

        status = AutoItemsConsumableProfileStatus.Ready;
        return family;
    }

    private static CostSummary SumCosts(
        PublicationTable<WorldConsumableCost> costs,
        Guid consumableId,
        WorldConsumableCostKind kind)
    {
        if (!WorldConsumableCostLookup.TryFindRange(
                costs, consumableId, kind, out var start, out var count))
        {
            return new CostSummary(true, false, false, BigDouble.Zero);
        }

        var toxicity = BigDouble.Zero;
        var hasToxicity = false;
        var hasOther = false;
        for (var index = 0; index < count; index++)
        {
            var cost = costs[start + index];
            if (!IsValidCost(cost.Amount))
                return new CostSummary(false, false, false, BigDouble.Zero);

            if (cost.ResourceId == KnownEntities.PotionToxicity.Uuid)
            {
                toxicity += cost.Amount;
                hasToxicity = true;
            }
            else
            {
                hasOther = true;
            }
        }

        return new CostSummary(true, hasToxicity, hasOther, toxicity);
    }

    private static bool IsValidCost(BigDouble amount) =>
        !BigDouble.IsNaN(amount) &&
        !BigDouble.IsInfinity(amount) &&
        amount.CompareTo(BigDouble.Zero) >= 0;

    private static AutoItemsConsumableProfile Failure(
        AutoItemsConsumableProfileStatus status,
        AutoItemsConsumableFamily family = AutoItemsConsumableFamily.Unknown,
        in WorldConsumable consumable = default,
        in WorldResource toxicity = default) =>
        new AutoItemsConsumableProfile(
            status,
            family,
            in consumable,
            in toxicity,
            BigDouble.Zero,
            BigDouble.Zero,
            hasHeldToxicityCost: false,
            hasAdditionalImmediateCosts: false,
            hasAdditionalHeldCosts: false);

    private readonly struct CostSummary
    {
        internal CostSummary(
            bool isValid,
            bool hasToxicity,
            bool hasOtherResource,
            BigDouble toxicity)
        {
            IsValid = isValid;
            HasToxicity = hasToxicity;
            HasOtherResource = hasOtherResource;
            Toxicity = toxicity;
        }

        internal bool IsValid { get; }
        internal bool HasToxicity { get; }
        internal bool HasOtherResource { get; }
        internal BigDouble Toxicity { get; }
    }
}
