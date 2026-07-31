using System;

namespace OrbAutomata;

/// <summary>
/// Revalidates the stack-backed native action queue immediately before Auto Buy mutates it.
/// Background WORLD remains the planning source; this port only closes the publication-to-action
/// race at the Unity-main-thread boundary.
/// </summary>
internal interface IAutoBuyQueueIntegrityPort
{
    bool TryReadHealthy(out bool healthy, out string reason);
}

internal sealed class AutoBuyNativeQueueIntegrityAdapter : IAutoBuyQueueIntegrityPort
{
    public bool TryReadHealthy(out bool healthy, out string reason)
    {
        healthy = false;
        reason = string.Empty;
        try
        {
            var manager = ActionManager.instance;
            var queue = manager?.actionableItems;
            if (queue is null)
            {
                reason = "ActionManager.actionableItems is unavailable";
                return false;
            }

            var total = 0;
            for (var index = 0; index < queue.value.Count; index++)
            {
                var actionable = queue.value[index];
                if (actionable is null || actionable.GetGuid() == Guid.Empty)
                {
                    reason = $"queue member {index} has no exact identity";
                    return true;
                }

                var stacks = queue.GetStacks(actionable);
                int pending;
                if (actionable.GetType() == typeof(StructureSO))
                {
                    pending = ((StructureSO)actionable).GetQueuedQuantity();
                }
                else if (actionable.GetType() == typeof(UpgradeSO))
                {
                    var upgrade = (UpgradeSO)actionable;
                    pending = checked(
                        upgrade.GetQueuedPurchaseLevel() - upgrade.GetPurchaseLevel());
                }
                else
                {
                    reason = $"queue member {actionable.GetGuid():D} has unsupported exact type " +
                        actionable.GetType().FullName;
                    return true;
                }

                if (stacks < 0 || pending < 0 || stacks != pending)
                {
                    reason = $"queue member {actionable.GetGuid():D} reports " +
                        $"stacks={stacks}, pending={pending}";
                    return true;
                }
                total = checked(total + stacks);
            }

            var nativeTotal = queue.GetTotalStacks();
            var remaining = queue.GetRemainingRoom();
            if (nativeTotal != total || remaining < 0 || queue.HasRoom() != (remaining > 0))
            {
                reason = $"queue totals disagree: members={total}, native={nativeTotal}, " +
                    $"remaining={remaining}, hasRoom={queue.HasRoom()}";
                return true;
            }

            healthy = true;
            reason = "native queue members and stacked occupancy agree";
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException || ex is ArgumentException ||
            ex is ArithmeticException || ex is NullReferenceException)
        {
            reason = ex.GetBaseException().GetType().Name;
            return false;
        }
    }
}

internal sealed class AutoBuyPermissiveQueueIntegrityPort : IAutoBuyQueueIntegrityPort
{
    internal static AutoBuyPermissiveQueueIntegrityPort Instance { get; } = new();

    private AutoBuyPermissiveQueueIntegrityPort()
    {
    }

    public bool TryReadHealthy(out bool healthy, out string reason)
    {
        healthy = true;
        reason = "isolated adapter default";
        return true;
    }
}
