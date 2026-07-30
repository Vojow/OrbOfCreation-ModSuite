using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Plans at most one use from one immutable world publication. There is no feature-local timer,
/// cooldown, continuation cursor, or candidate memory.
/// </summary>
internal static class AutoItemsCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        out AutoItemsDecisionMetrics metrics)
    {
        if (!AutoItemsConfigurationPolicy.IsOperational(configuration))
        {
            metrics = new AutoItemsDecisionMetrics(
                world.Consumables.Count, 0, 0, 0, 0, AutoItemsDecisionKind.Disabled);
            return WakePolicy.OnPublication;
        }

        var rows = world.Consumables.AsSpan();
        var rejected = 0;
        var eligibleRelics = 0;
        var eligibleScrolls = 0;
        AutoItemsCycleAction firstRelic = default;
        AutoItemsCycleAction firstScroll = default;
        for (var index = 0; index < rows.Length; index++)
        {
            var profile = AutoItemsConsumableProfileBuilder.Build(
                world,
                rows[index].ConsumableId);
            if (!profile.IsReady)
            {
                rejected++;
                continue;
            }
            if (!AutoItemsConfigurationPolicy.Allows(configuration.AutoItems, profile.Family) ||
                !IsWorldEligible(in profile))
                continue;

            if (profile.Family == AutoItemsConsumableFamily.Relic)
            {
                eligibleRelics++;
                if (firstRelic.ItemId == System.Guid.Empty)
                    firstRelic = new AutoItemsCycleAction(
                        profile.Consumable.ConsumableId,
                        profile.Family,
                        world.CollectedAtEpoch,
                        plannedLevel: 0);
                continue;
            }

            if (!profile.Consumable.CanBeRandomized ||
                !WorldConsumableCountLookup.TryGetStrongestOwnedLevel(
                    world.ConsumableCounts,
                    profile.Consumable.ConsumableId,
                    out var strongestLevel))
                continue;
            eligibleScrolls++;
            if (firstScroll.ItemId == System.Guid.Empty)
                firstScroll = new AutoItemsCycleAction(
                    profile.Consumable.ConsumableId,
                    profile.Family,
                    world.CollectedAtEpoch,
                    strongestLevel);
        }

        if (firstRelic.ItemId != System.Guid.Empty)
            return Plan(
                in firstRelic,
                rows.Length,
                rejected,
                eligibleRelics,
                eligibleScrolls,
                AutoItemsDecisionKind.Relic,
                actions,
                out metrics);
        if (firstScroll.ItemId != System.Guid.Empty)
            return Plan(
                in firstScroll,
                rows.Length,
                rejected,
                eligibleRelics,
                eligibleScrolls,
                AutoItemsDecisionKind.Scroll,
                actions,
                out metrics);

        metrics = new AutoItemsDecisionMetrics(
            rows.Length,
            rejected,
            eligibleRelics,
            eligibleScrolls,
            0,
            AutoItemsDecisionKind.Idle);
        return WakePolicy.OnPublication;
    }

    private static bool IsWorldEligible(in AutoItemsConsumableProfile profile) =>
        profile.Consumable.Visible &&
        profile.Consumable.Quantity - profile.Consumable.QueuedQuantity > 0 &&
        profile.Consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) <= 0 &&
        profile.Consumable.CurrentCooldown.CompareTo(BigDouble.Zero) <= 0 &&
        profile.Toxicity.TrueQuantity.CompareTo(profile.ImmediateToxicityCost) >= 0;

    private static WakePolicy Plan(
        in AutoItemsCycleAction action,
        int captured,
        int rejected,
        int eligibleRelics,
        int eligibleScrolls,
        AutoItemsDecisionKind kind,
        ServiceActionWriter<AutoItemsCycleAction> actions,
        out AutoItemsDecisionMetrics metrics)
    {
        actions.Add(action);
        metrics = new AutoItemsDecisionMetrics(
            captured,
            rejected,
            eligibleRelics,
            eligibleScrolls,
            1,
            kind);
        return WakePolicy.OnPublication;
    }
}
