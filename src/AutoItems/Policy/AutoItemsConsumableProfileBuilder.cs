using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoItemsConsumableProfileStatus
{
    Ready = 0,
    ConsumableMissing = 1,
    NoSupportedFamily = 2,
    IncoherentSupportedFamilies = 3,
    ToxicityResourceMissing = 4,
    ToxicityResourceNotInverted = 5,
    ToxicityCapacityUnavailable = 6,
    ToxicityCostMissing = 7,
    CostAmountInvalid = 8,
}

/// <summary>
/// The complete native-free policy view of one supported consumable. Resource-cost categories
/// remain explicit so temporary-item policy can reject any additional immediate or held category;
/// live <c>CanFire()</c> remains authoritative for native affordability.
/// </summary>
internal readonly struct AutoItemsConsumableProfile
{
    internal AutoItemsConsumableProfile(
        AutoItemsConsumableProfileStatus status,
        AutoItemsConsumableFamily family,
        in WorldConsumable consumable,
        in WorldResource toxicity,
        BigDouble immediateToxicityCost,
        bool hasAdditionalImmediateCosts,
        bool hasAdditionalHeldCosts)
    {
        Status = status;
        Family = family;
        Consumable = consumable;
        Toxicity = toxicity;
        ImmediateToxicityCost = immediateToxicityCost;
        HasAdditionalImmediateCosts = hasAdditionalImmediateCosts;
        HasAdditionalHeldCosts = hasAdditionalHeldCosts;
    }

    internal AutoItemsConsumableProfileStatus Status { get; }
    internal bool IsReady => Status == AutoItemsConsumableProfileStatus.Ready;
    internal AutoItemsConsumableFamily Family { get; }
    internal WorldConsumable Consumable { get; }
    internal WorldResource Toxicity { get; }
    internal BigDouble ImmediateToxicityCost { get; }
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

        var family = Classify(world.ConsumableTypes, consumableId, out var status);
        if (status != AutoItemsConsumableProfileStatus.Ready)
            return Failure(status, family, in consumable);

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

        var families = new AutoItemsConsumableFamilySet();
        for (var index = 0; index < count; index++)
        {
            var typeId = types[start + index].TypeId;
            if (typeId == Guid.Empty)
            {
                status = AutoItemsConsumableProfileStatus.IncoherentSupportedFamilies;
                return AutoItemsConsumableFamily.Unknown;
            }
            for (var previous = 0; previous < index; previous++)
            {
                if (types[start + previous].TypeId != typeId) continue;
                status = AutoItemsConsumableProfileStatus.IncoherentSupportedFamilies;
                return AutoItemsConsumableFamily.Unknown;
            }
            var candidate = AutoItemsConsumableFamilies.FromTypeId(typeId);
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            if (families.TryAdd(candidate)) continue;
            status = AutoItemsConsumableProfileStatus.IncoherentSupportedFamilies;
            return AutoItemsConsumableFamily.Unknown;
        }
        if (families.Count == 0)
        {
            status = AutoItemsConsumableProfileStatus.NoSupportedFamily;
            return AutoItemsConsumableFamily.Unknown;
        }
        if (!families.TryResolveExecutionFamily(out var family))
        {
            status = AutoItemsConsumableProfileStatus.IncoherentSupportedFamilies;
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
            return new CostSummary(true, false, false, BigDouble.Zero);

        var toxicity = BigDouble.Zero;
        var hasToxicity = false;
        var hasOther = false;
        for (var index = 0; index < count; index++)
        {
            var cost = costs[start + index];
            if (BigDouble.IsNaN(cost.Amount) ||
                BigDouble.IsInfinity(cost.Amount) ||
                cost.Amount.CompareTo(BigDouble.Zero) < 0)
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

    private static AutoItemsConsumableProfile Failure(
        AutoItemsConsumableProfileStatus status,
        AutoItemsConsumableFamily family = AutoItemsConsumableFamily.Unknown,
        in WorldConsumable consumable = default,
        in WorldResource toxicity = default) =>
        new(
            status,
            family,
            in consumable,
            in toxicity,
            BigDouble.Zero,
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
