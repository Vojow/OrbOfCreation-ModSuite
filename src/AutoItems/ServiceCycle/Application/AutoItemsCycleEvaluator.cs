using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoItemsCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        AutoItemsTemporaryActivationTracker temporaryActivations,
        out AutoItemsDecisionMetrics metrics)
    {
        var interval = AutoItemsConfigurationPolicy.EvaluationInterval(config);
        if (!AutoItemsConfigurationPolicy.IsOperational(config))
        {
            state.EndRecoveryWait();
            metrics = new AutoItemsDecisionMetrics(
                world.Consumables.Count, 0, 0, 0, 0, 0, AutoItemsDecisionKind.Disabled);
            return WakePolicy.AfterDecision(interval);
        }

        var activation = temporaryActivations.Observe(world, out _);
        if (activation is AutoItemsTemporaryActivationState.AwaitingActivation or
            AutoItemsTemporaryActivationState.Active)
        {
            metrics = new AutoItemsDecisionMetrics(
                world.Consumables.Count,
                0,
                0,
                0,
                0,
                0,
                activation == AutoItemsTemporaryActivationState.Active
                    ? AutoItemsDecisionKind.TemporaryEffectActive
                    : AutoItemsDecisionKind.AwaitingTemporaryActivation);
            return WakePolicy.AfterDecision(interval);
        }
        if (activation == AutoItemsTemporaryActivationState.Quarantined)
        {
            metrics = new AutoItemsDecisionMetrics(
                world.Consumables.Count,
                0,
                0,
                0,
                0,
                0,
                AutoItemsDecisionKind.TemporaryItemQuarantined);
            return WakePolicy.AfterDecision(interval);
        }

        var rows = world.Consumables.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].QueuedQuantity <= 0 &&
                rows[index].CurrentPrepTime.CompareTo(BigDouble.Zero) <= 0)
                continue;
            metrics = new AutoItemsDecisionMetrics(
                rows.Length, 0, 0, 0, 0, 0, AutoItemsDecisionKind.NativeBusy);
            return WakePolicy.AfterDecision(interval);
        }

        var rejected = 0;
        var temporary = 0;
        var eligibleRelics = 0;
        var eligibleScrolls = 0;
        var hasToxicityReading =
            WorldLookup.TryFind(
                world.Resources,
                KnownEntities.PotionToxicity.Uuid,
                out var toxicity) &&
            toxicity.IsCapped &&
            toxicity.Reading.Traits.InvertedResource;
        var toxicityAtZero = hasToxicityReading && toxicity.IsAtCapacity;
        if (state.RecoveryWaitActive)
        {
            if (toxicityAtZero)
                state.EndRecoveryWait();
            else if (hasToxicityReading)
            {
                metrics = new AutoItemsDecisionMetrics(
                    rows.Length,
                    rejected,
                    temporary,
                    eligibleRelics,
                    eligibleScrolls,
                    0,
                    AutoItemsDecisionKind.WaitingForToxicityRecovery);
                return WakePolicy.AfterDecision(interval);
            }
        }
        AutoItemsCycleAction firstRelic = default;
        AutoItemsCycleAction firstScroll = default;
        AutoItemsCycleAction firstTemporary = default;
        var eligibleTemporary = 0;
        var saturationCandidate = false;
        var temporaryUsagePresent = false;
        var allowlist =
            AutoItemsTemporaryItemPolicy.ParseAllowlist(
                config.AutoItems.TemporaryItemAllowlist);

        for (var index = 0; index < rows.Length; index++)
        {
            var profile = AutoItemsConsumableProfiler.Build(world, rows[index].ConsumableId);
            if (!profile.IsReady)
            {
                rejected++;
                continue;
            }

            if (profile.Family is AutoItemsConsumableFamily.Fruit or AutoItemsConsumableFamily.Potion)
            {
                temporary++;
                if (WorldConsumableUsageLookup.TryFindRange(
                        world.ConsumableUsages,
                        profile.Consumable.ConsumableId,
                        out var usageStart,
                        out var usageCount))
                {
                    for (var usageIndex = 0; usageIndex < usageCount; usageIndex++)
                    {
                        if (!world.ConsumableUsages[usageStart + usageIndex].Expired)
                        {
                            temporaryUsagePresent = true;
                            break;
                        }
                    }
                }
                if (!AutoItemsTemporaryItemPolicy.IsFamilyEnabled(
                        config.AutoItems,
                        profile.Family) ||
                    !allowlist.Contains(profile.Consumable.ConsumableId) ||
                    temporaryActivations.IsQuarantined(profile.Consumable.ConsumableId) ||
                    !IsBaseEligible(in profile) ||
                    !HasSafeTemporaryShape(in profile))
                    continue;

                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleTemporary++;
                if (firstTemporary.ItemId == System.Guid.Empty)
                {
                    var temporaryBelief = new AutoItemsPlanBelief(
                        profile.Consumable.Quantity,
                        profile.Consumable.QueuedQuantity,
                        profile.Consumable.Randomized,
                        profile.Consumable.CanBeRandomized);
                    firstTemporary = new AutoItemsCycleAction(
                        profile.Consumable.ConsumableId,
                        profile.Family,
                        world.CollectedAtFrame,
                        world.CollectedAtEpoch,
                        in temporaryBelief);
                }
                continue;
            }
            if (!IsBaseEligible(in profile)) continue;

            var belief = new AutoItemsPlanBelief(
                profile.Consumable.Quantity,
                profile.Consumable.QueuedQuantity,
                profile.Consumable.Randomized,
                profile.Consumable.CanBeRandomized);
            if (profile.Family == AutoItemsConsumableFamily.Relic && config.AutoItems.UseRelics)
            {
                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleRelics++;
                if (firstRelic.ItemId == System.Guid.Empty)
                    firstRelic = new AutoItemsCycleAction(
                        profile.Consumable.ConsumableId,
                        profile.Family,
                        world.CollectedAtFrame,
                        world.CollectedAtEpoch,
                        in belief);
            }
            else if (
                profile.Family == AutoItemsConsumableFamily.Scroll &&
                config.AutoItems.UseScrolls &&
                profile.Consumable.CanBeRandomized)
            {
                saturationCandidate |= CanFitAfterFullRecovery(in profile);
                if (!HasCurrentToxicityHeadroom(in profile)) continue;
                eligibleScrolls++;
                if (firstScroll.ItemId == System.Guid.Empty)
                    firstScroll = new AutoItemsCycleAction(
                        profile.Consumable.ConsumableId,
                        profile.Family,
                        world.CollectedAtFrame,
                        world.CollectedAtEpoch,
                        in belief);
            }
        }

        if (eligibleRelics > 0)
            return Plan(
                in firstRelic, rows.Length, rejected, temporary, eligibleRelics, eligibleScrolls,
                AutoItemsDecisionKind.Relic, actions, out metrics);
        if (temporaryUsagePresent)
        {
            metrics = new AutoItemsDecisionMetrics(
                rows.Length,
                rejected,
                temporary,
                eligibleRelics,
                eligibleScrolls,
                0,
                AutoItemsDecisionKind.TemporaryEffectActive);
            return WakePolicy.AfterDecision(interval);
        }
        if (eligibleTemporary > 0)
            return Plan(
                in firstTemporary,
                rows.Length,
                rejected,
                temporary,
                eligibleRelics,
                eligibleScrolls,
                AutoItemsDecisionKind.TemporaryItem,
                actions,
                out metrics);
        if (eligibleScrolls > 0)
            return Plan(
                in firstScroll, rows.Length, rejected, temporary, eligibleRelics, eligibleScrolls,
                AutoItemsDecisionKind.Scroll, actions, out metrics);
        if (saturationCandidate && hasToxicityReading && !toxicityAtZero)
        {
            state.BeginRecoveryWait();
            metrics = new AutoItemsDecisionMetrics(
                rows.Length,
                rejected,
                temporary,
                eligibleRelics,
                eligibleScrolls,
                0,
                AutoItemsDecisionKind.WaitingForToxicityRecovery);
            return WakePolicy.AfterDecision(interval);
        }

        metrics = new AutoItemsDecisionMetrics(
            rows.Length, rejected, temporary, eligibleRelics, eligibleScrolls, 0,
            AutoItemsDecisionKind.Idle);
        return WakePolicy.AfterDecision(interval);
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

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        int captured,
        int rejected,
        int temporary,
        int relics,
        int scrolls,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        metrics = new AutoItemsDecisionMetrics(
            captured, rejected, temporary, relics, scrolls, 1, kind);
        return WakePolicy.Immediate;
    }
}
