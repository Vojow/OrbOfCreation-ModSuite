using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>Pure Auto Buy admission over the queue facts in the pinned background WORLD.</summary>
internal static class AutoBuyWorldQueueIntegrity
{
    internal static bool IsHealthy(GameWorldState world, out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        // Hand-authored unit worlds predate category-status publication. Production worlds always
        // carry the status atomically; once it is present, missing/partial queue evidence fails
        // closed. Keeping the status-less case neutral avoids turning unrelated fixtures into a
        // second fake queue model.
        var categories = world.CollectionCategories.AsSpan();
        var queueCategoryPresent = false;
        for (var index = 0; index < categories.Length; index++)
        {
            if (!string.Equals(categories[index].Category, "action queues", StringComparison.Ordinal))
                continue;
            queueCategoryPresent = true;
            if (!categories[index].IsClean)
            {
                reason = "the action-queue category was not collected cleanly";
                return false;
            }
            break;
        }
        if (!queueCategoryPresent)
        {
            reason = "no category-status row (synthetic/legacy WORLD)";
            return true;
        }

        if (!WorldLookup.TryFind(
                world.ActionQueues,
                KnownEntities.ActiveActionables.Uuid,
                out var queue) ||
            queue.Kind != WorldActionQueueKind.Stacked)
        {
            reason = "the stacked action queue is absent from the pinned WORLD";
            return false;
        }
        if (!queue.Consistent || queue.TotalStacks < 0 || queue.RemainingStackRoom < 0)
        {
            reason = "the stacked action-queue summary contradicts its native occupancy";
            return false;
        }

        var seen = new HashSet<Guid>();
        var stacks = 0;
        var members = world.ActionQueueMembers.AsSpan();
        for (var index = 0; index < members.Length; index++)
        {
            ref readonly var member = ref members[index];
            if (member.QueueId != queue.QueueId) continue;
            if (member.ActionableId == Guid.Empty || !seen.Add(member.ActionableId) ||
                !member.TimingReadable ||
                member.Consistency != WorldActionQueueMemberConsistency.Consistent)
            {
                reason = $"action-queue member {member.ActionableId:D} is unknown, duplicated, " +
                    "unreadable, or inconsistent in the pinned WORLD";
                return false;
            }
            stacks = checked(stacks + member.StackCount);
        }

        if (seen.Count != queue.SlotCount || stacks != queue.TotalStacks)
        {
            reason = $"action-queue members disagree with the summary: unique={seen.Count}/" +
                $"{queue.SlotCount}, stacks={stacks}/{queue.TotalStacks}";
            return false;
        }

        reason = "pinned WORLD queue members and stacked occupancy agree";
        return true;
    }
}
