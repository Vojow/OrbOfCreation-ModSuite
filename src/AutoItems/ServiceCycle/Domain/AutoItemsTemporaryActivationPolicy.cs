using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoItemsTemporaryActivationState
{
    None = 0,
    AwaitingActivation = 1,
    Active = 2,
    Completed = 3,
    Quarantined = 4,
}

/// <summary>
/// Reconciles a verified service-cycle receipt with later immutable world publications. All memory
/// belongs to the lifecycle-scoped worker state; no mutable tracker crosses the thread boundary.
/// </summary>
internal static class AutoItemsTemporaryActivationPolicy
{
    internal static void ReconcileReceipt(
        in BatchReceipt receipt,
        ref AutoItemsCycleState state)
    {
        if (!state.HasPendingReceipt || !receipt.IsPresent) return;
        var planned = state.PendingReceiptAction;
        state.ClearPendingReceipt();
        if (receipt.CommittedCount == 1 &&
            AutoItemsConsumableFamilies.IsTemporary(planned.Family))
        {
            state.RecordSubmittedTemporary(in planned);
        }
    }

    internal static AutoItemsTemporaryActivationState Observe(
        GameWorldState world,
        ref AutoItemsCycleState state,
        out Guid itemId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        itemId = state.PendingTemporaryItem;
        if (itemId == Guid.Empty) return AutoItemsTemporaryActivationState.None;

        var usageCount = 0;
        var engaged = false;
        var expired = false;
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
                engaged |= usage.Engaged;
                expired |= usage.Expired;
            }
        }

        if (usageCount > 1 || expired)
            return QuarantinePending(ref state, out itemId);
        if (usageCount == 1)
        {
            if (engaged)
            {
                state.MarkTemporaryActivationSeen();
                return AutoItemsTemporaryActivationState.Active;
            }
            return AutoItemsTemporaryActivationState.AwaitingActivation;
        }

        if (world.CollectedAtFrame <= state.TemporarySubmittedFromFrame)
            return AutoItemsTemporaryActivationState.AwaitingActivation;
        if (WorldLookup.TryFind(world.Consumables, itemId, out var consumable) &&
            (consumable.QueuedQuantity > 0 ||
             consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) > 0))
        {
            return AutoItemsTemporaryActivationState.AwaitingActivation;
        }

        if (!state.TemporaryActivationSeen)
            return QuarantinePending(ref state, out itemId);

        state.ClearPendingTemporary();
        return AutoItemsTemporaryActivationState.Completed;
    }

    private static AutoItemsTemporaryActivationState QuarantinePending(
        ref AutoItemsCycleState state,
        out Guid itemId)
    {
        itemId = state.PendingTemporaryItem;
        state.QuarantinePendingTemporary();
        return AutoItemsTemporaryActivationState.Quarantined;
    }
}
