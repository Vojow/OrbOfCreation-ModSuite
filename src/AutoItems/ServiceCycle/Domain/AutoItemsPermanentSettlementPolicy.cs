using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoItemsPermanentSettlementState
{
    None = 0,
    AwaitingSettlement = 1,
    Completed = 2,
    Quarantined = 3,
}

internal enum AutoItemsPermanentQuarantineCause
{
    None = 0,
    ItemDisappeared = 1,
    MultipleUsages = 2,
    UsageLevelChanged = 3,
    MissingUsageDuringPreparation = 4,
    QueueStuckWithoutUsage = 5,
    QueueClearedWhileUsagePresent = 6,
    QueueOverflow = 7,
    InvalidQueue = 8,
    EngagedUsage = 9,
    ExpiredUsage = 10,
    PreparationMissingDuringQueuedUsage = 11,
    PreparationWithoutQueue = 12,
}

internal readonly struct AutoItemsPermanentSettlementObservation
{
    internal AutoItemsPermanentSettlementObservation(
        AutoItemsPermanentSettlementState state,
        Guid itemId,
        AutoItemsConsumableFamily family,
        AutoItemsPermanentQuarantineCause quarantineCause)
    {
        State = state;
        ItemId = itemId;
        Family = family;
        QuarantineCause = quarantineCause;
    }

    internal AutoItemsPermanentSettlementState State { get; }
    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal AutoItemsPermanentQuarantineCause QuarantineCause { get; }
}

/// <summary>
/// Follows a committed Scroll or Relic until the game publishes a settled queue. The mutation
/// boundary proves the exact usage level synchronously; this policy catches later preparation or
/// completion failures without repairing game-owned queue fields.
/// </summary>
internal static class AutoItemsPermanentSettlementPolicy
{
    internal const string CollectionCategory = "consumables";

    internal static void ReconcileReceipt(
        in BatchReceipt receipt,
        ref AutoItemsCycleState state)
    {
        if (!state.HasPendingReceipt || !receipt.IsPresent) return;
        var planned = state.PendingReceiptAction;
        state.ClearPendingReceipt();
        if (receipt.ActionCount != 1 || receipt.CommittedCount != 1) return;
        if (AutoItemsConsumableFamilies.IsTemporary(planned.Family))
            state.RecordSubmittedTemporary(in planned);
        else
            state.RecordSubmittedPermanent(in planned);
    }

    internal static AutoItemsPermanentSettlementObservation Observe(
        GameWorldState world,
        ref AutoItemsCycleState state)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var itemId = state.PendingPermanentItem;
        var family = state.PendingPermanentFamily;
        if (itemId == Guid.Empty)
            return Observation(AutoItemsPermanentSettlementState.None);

        if (world.CollectedAtFrame <= state.PermanentSubmittedFromFrame ||
            !IsConsumablesCategoryClean(world))
        {
            return Observation(
                AutoItemsPermanentSettlementState.AwaitingSettlement,
                itemId,
                family);
        }

        if (!WorldLookup.TryFind(world.Consumables, itemId, out var consumable))
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.ItemDisappeared);

        var usageCount = 0;
        var usageLevel = 0;
        var usageEngaged = false;
        var usageExpired = false;
        if (WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                itemId,
                out var start,
                out var count))
        {
            usageCount = count;
            for (var index = 0; index < count; index++)
            {
                var usage = world.ConsumableUsages[start + index];
                if (index == 0) usageLevel = usage.Level;
                usageEngaged |= usage.Engaged;
                usageExpired |= usage.Expired;
            }
        }

        if (consumable.QueuedQuantity > 1)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.QueueOverflow);
        if (consumable.QueuedQuantity < 0)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.InvalidQueue);
        if (usageCount > 1)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.MultipleUsages);
        if (usageExpired)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.ExpiredUsage);
        if (usageEngaged)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.EngagedUsage);
        if (usageCount == 1 && usageLevel != state.PermanentPlannedLevel)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.UsageLevelChanged);

        var preparationActive =
            consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) > 0;
        if (consumable.QueuedQuantity == 1 && preparationActive && usageCount == 1)
        {
            state.MarkPermanentUsageSeen();
            return Observation(
                AutoItemsPermanentSettlementState.AwaitingSettlement,
                itemId,
                family);
        }

        if (consumable.QueuedQuantity == 0 && !preparationActive && usageCount == 0)
        {
            state.ClearPendingPermanent();
            return Observation(
                AutoItemsPermanentSettlementState.Completed,
                itemId,
                family);
        }

        if (usageCount == 1 && consumable.QueuedQuantity == 0)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.QueueClearedWhileUsagePresent);
        if (consumable.QueuedQuantity == 1 && usageCount == 0)
        {
            var cause = preparationActive
                ? AutoItemsPermanentQuarantineCause.MissingUsageDuringPreparation
                : AutoItemsPermanentQuarantineCause.QueueStuckWithoutUsage;
            return Quarantine(ref state, itemId, family, cause);
        }
        if (consumable.QueuedQuantity == 1 && usageCount == 1)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.PreparationMissingDuringQueuedUsage);
        if (consumable.QueuedQuantity == 0 && preparationActive)
            return Quarantine(
                ref state,
                itemId,
                family,
                AutoItemsPermanentQuarantineCause.PreparationWithoutQueue);

        throw new InvalidOperationException("Unhandled permanent consumable settlement topology.");
    }

    private static AutoItemsPermanentSettlementObservation Quarantine(
        ref AutoItemsCycleState state,
        Guid itemId,
        AutoItemsConsumableFamily family,
        AutoItemsPermanentQuarantineCause cause)
    {
        state.QuarantinePendingPermanent(cause);
        return Observation(
            AutoItemsPermanentSettlementState.Quarantined,
            itemId,
            family,
            cause);
    }

    private static AutoItemsPermanentSettlementObservation Observation(
        AutoItemsPermanentSettlementState state,
        Guid itemId = default,
        AutoItemsConsumableFamily family = AutoItemsConsumableFamily.Unknown,
        AutoItemsPermanentQuarantineCause cause = AutoItemsPermanentQuarantineCause.None) =>
        new(state, itemId, family, cause);

    private static bool IsConsumablesCategoryClean(GameWorldState world)
    {
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var category = world.CollectionCategories[index];
            if (string.Equals(category.Category, CollectionCategory, StringComparison.Ordinal))
                return category.IsClean;
        }

        return false;
    }
}
