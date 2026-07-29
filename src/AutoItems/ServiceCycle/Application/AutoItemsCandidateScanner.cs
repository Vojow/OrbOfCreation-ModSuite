using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The result of one immutable-world candidate scan. Selection stays deterministic because world
/// consumables are ordered by stable UUID and only the first eligible member of each family wins.
/// </summary>
internal readonly struct AutoItemsCandidateScan
{
    internal AutoItemsCandidateScan(
        int captured,
        int rejectedProfiles,
        int temporaryItems,
        int eligibleTemporaryItems,
        int eligibleRelics,
        int eligibleScrolls,
        bool temporaryUsagePresent,
        bool saturationCandidate,
        in AutoItemsCycleAction firstTemporary,
        in AutoItemsCycleAction firstRelic,
        in AutoItemsCycleAction firstScroll)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        TemporaryItems = temporaryItems;
        EligibleTemporaryItems = eligibleTemporaryItems;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        TemporaryUsagePresent = temporaryUsagePresent;
        SaturationCandidate = saturationCandidate;
        FirstTemporary = firstTemporary;
        FirstRelic = firstRelic;
        FirstScroll = firstScroll;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int TemporaryItems { get; }
    internal int EligibleTemporaryItems { get; }
    internal int EligibleRelics { get; }
    internal int EligibleScrolls { get; }
    internal bool TemporaryUsagePresent { get; }
    internal bool SaturationCandidate { get; }
    internal AutoItemsCycleAction FirstTemporary { get; }
    internal AutoItemsCycleAction FirstRelic { get; }
    internal AutoItemsCycleAction FirstScroll { get; }

    internal AutoItemsDecisionMetrics ToMetrics(
        AutoItemsDecisionKind kind,
        int plannedActions = 0) =>
        new(
            Captured,
            RejectedProfiles,
            TemporaryItems,
            EligibleRelics,
            EligibleScrolls,
            plannedActions,
            kind);
}

/// <summary>
/// Performs the pure item-profile and eligibility pass. Lifecycle, activation, native-busy, and
/// recovery gates remain in the evaluator so scan results cannot bypass those state transitions.
/// </summary>
internal static class AutoItemsCandidateScanner
{
    internal static AutoItemsCandidateScan Scan(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        AutoItemsTemporaryActivationTracker temporaryActivations,
        ISet<Guid>? temporaryAllowlist)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (temporaryActivations is null)
            throw new ArgumentNullException(nameof(temporaryActivations));

        var rows = world.Consumables.AsSpan();
        var rejected = 0;
        var temporary = 0;
        var eligibleTemporary = 0;
        var eligibleRelics = 0;
        var eligibleScrolls = 0;
        var temporaryUsagePresent = false;
        var saturationCandidate = false;
        AutoItemsCycleAction firstTemporary = default;
        AutoItemsCycleAction firstRelic = default;
        AutoItemsCycleAction firstScroll = default;

        for (var index = 0; index < rows.Length; index++)
        {
            var hasTemporaryFamily = HasTemporaryFamily(
                world,
                rows[index].ConsumableId);
            if (hasTemporaryFamily)
            {
                temporary++;
                temporaryUsagePresent |= HasPendingOrActiveUsage(
                    world,
                    rows[index].ConsumableId);
            }

            var profile = AutoItemsConsumableProfileBuilder.Build(
                world,
                rows[index].ConsumableId);
            if (!profile.IsReady)
            {
                rejected++;
                continue;
            }

            if (AutoItemsConsumableFamilies.IsTemporary(profile.Family))
            {
                if (!AutoItemsConfigurationPolicy.Allows(
                        configuration.AutoItems,
                        profile.Family,
                        profile.Consumable.ConsumableId,
                        temporaryAllowlist) ||
                    temporaryActivations.IsQuarantined(profile.Consumable.ConsumableId) ||
                    !IsBaseEligible(in profile) ||
                    !HasSafeTemporaryShape(in profile))
                {
                    continue;
                }

                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleTemporary++;
                CaptureFirst(
                    ref firstTemporary,
                    in profile,
                    world.CollectedAtFrame,
                    world.CollectedAtEpoch);
                continue;
            }

            if (!IsBaseEligible(in profile)) continue;
            if (profile.Family == AutoItemsConsumableFamily.Relic &&
                AutoItemsConfigurationPolicy.Allows(
                    configuration.AutoItems,
                    profile.Family,
                    profile.Consumable.ConsumableId,
                    temporaryAllowlist))
            {
                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleRelics++;
                CaptureFirst(
                    ref firstRelic,
                    in profile,
                    world.CollectedAtFrame,
                    world.CollectedAtEpoch);
            }
            else if (
                profile.Family == AutoItemsConsumableFamily.Scroll &&
                AutoItemsConfigurationPolicy.Allows(
                    configuration.AutoItems,
                    profile.Family,
                    profile.Consumable.ConsumableId,
                    temporaryAllowlist) &&
                profile.Consumable.CanBeRandomized)
            {
                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleScrolls++;
                CaptureFirst(
                    ref firstScroll,
                    in profile,
                    world.CollectedAtFrame,
                    world.CollectedAtEpoch);
            }
        }

        return new AutoItemsCandidateScan(
            rows.Length,
            rejected,
            temporary,
            eligibleTemporary,
            eligibleRelics,
            eligibleScrolls,
            temporaryUsagePresent,
            saturationCandidate,
            in firstTemporary,
            in firstRelic,
            in firstScroll);
    }

    private static bool HasTemporaryFamily(GameWorldState world, Guid consumableId)
    {
        if (!WorldConsumableTypeLookup.TryFindRange(
                world.ConsumableTypes,
                consumableId,
                out var start,
                out var count))
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (AutoItemsConsumableFamilies.IsTemporary(
                    AutoItemsConsumableFamilies.FromTypeId(
                        world.ConsumableTypes[start + index].TypeId)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasPendingOrActiveUsage(GameWorldState world, Guid consumableId)
    {
        if (!WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                consumableId,
                out var start,
                out var count))
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            if (!world.ConsumableUsages[start + index].Expired) return true;
        }
        return false;
    }

    private static void CaptureFirst(
        ref AutoItemsCycleAction first,
        in AutoItemsConsumableProfile profile,
        long collectedAtFrame,
        long collectedAtEpoch)
    {
        if (first.ItemId != Guid.Empty) return;
        first = new AutoItemsCycleAction(
            profile.Consumable.ConsumableId,
            profile.Family,
            collectedAtFrame,
            collectedAtEpoch);
    }

    private static bool IsBaseEligible(in AutoItemsConsumableProfile profile) =>
        profile.Consumable.Visible &&
        profile.Consumable.Quantity - profile.Consumable.QueuedQuantity > 0 &&
        profile.Consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) <= 0 &&
        profile.Consumable.CurrentCooldown.CompareTo(BigDouble.Zero) <= 0;

    private static bool HasCurrentToxicityHeadroom(in AutoItemsConsumableProfile profile) =>
        profile.Toxicity.TrueQuantity.CompareTo(profile.ImmediateToxicityCost) >= 0;

    private static bool CanFitAfterFullRecovery(in AutoItemsConsumableProfile profile)
    {
        var trueCapacity =
            profile.Toxicity.Reading.Capacity *
            OrbGameMath.AsPercent(profile.Toxicity.Reading.Quality);
        return !BigDouble.IsNaN(trueCapacity) &&
               !BigDouble.IsInfinity(trueCapacity) &&
               trueCapacity.CompareTo(profile.ImmediateToxicityCost) >= 0;
    }

    private static bool HasSafeTemporaryShape(in AutoItemsConsumableProfile profile) =>
        profile.Consumable.HasDuration &&
        profile.Consumable.DurationBase > 0d &&
        !double.IsNaN(profile.Consumable.DurationBase) &&
        !double.IsInfinity(profile.Consumable.DurationBase) &&
        !profile.HasAdditionalImmediateCosts &&
        !profile.HasAdditionalHeldCosts;
}
