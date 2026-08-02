using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoItemsConsumableUseGameAction
{
    internal ConsumablePlayerSubmission Submit(in ConsumablePlayerAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.WrongThread,
                "Consumable actions are bound to Unity thread " + _mainThreadId + ".");
        if (_quarantineReason.Length != 0)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.Quarantined,
                _quarantineReason);
        if (_playerBindings is not { } native)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.ContractUnavailable,
                _playerBindingFailure.Length == 0
                    ? "The lifecycle-scoped consumable player binding set is unavailable."
                    : _playerBindingFailure);

        long liveLifecycle;
        try
        {
            liveLifecycle = _readLifecycleEpoch();
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.LifecycleReplaced,
                "The live lifecycle could not be read: " + ex.GetBaseException().Message);
        }
        if (liveLifecycle != action.LifecycleEpoch)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.LifecycleReplaced,
                "Action lifecycle " + action.LifecycleEpoch +
                " is stale; live lifecycle is " + liveLifecycle + ".");

        var resolution = _registryResolver.Resolve(action.ConsumableId, native.ConsumableType);
        if (!resolution.IsResolved)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        try
        {
            return action.Kind switch
            {
                ConsumablePlayerActionKind.Use => PlayerUse(in action, item, native),
                ConsumablePlayerActionKind.Cancel => PlayerCancel(in action, item, native),
                ConsumablePlayerActionKind.Discard => PlayerDiscard(in action, item, native),
                ConsumablePlayerActionKind.SetRandomization =>
                    PlayerSetRandomization(in action, item, native),
                ConsumablePlayerActionKind.Move => PlayerMove(in action, item, native),
                _ => ConsumablePlayerSubmission.Reject(
                    in action,
                    ConsumablePlayerPreflight.ContractUnavailable,
                    "Unknown consumable action mode."),
            };
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.ContractUnavailable,
                "Consumable preflight failed before mutation: " + ex.GetBaseException().Message);
        }
    }

    private ConsumablePlayerSubmission PlayerUse(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.IsVisible(item))
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.NotVisible,
                "ConsumableSO.IsVisible() refused the exact UUID-resolved item.");
        if (native.IsTargeting())
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.TargetingInProgress,
                "A native target request is already pending.");
        if (!native.CanUse())
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.InventoryBusy,
                "Inventory.CanUseConsumable() refused while consumable preparation is busy.");
        if (!native.CanFire(item))
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.CanFireRefused,
                "ConsumableSO.CanFire() refused the exact UUID-resolved item.");
        var before = Capture(item, native);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out var reason))
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.MultiBuyUnavailable,
                reason);

        using (multiBuy)
        {
            try
            {
                native.SelectAndFire(item);
            }
            catch (Exception ex) when (Expected(ex))
            {
                return ObserveAfterThrow(
                    in action,
                    item,
                    native,
                    in before,
                    ConsumablePlayerNativeStage.Use,
                    1,
                    static (first, second) => second.Queued == first.Queued + 1,
                    "SelectAndFire threw: " + ex.GetBaseException().Message);
            }
        }
        return Verify(
            in action,
            item,
            native,
            in before,
            ConsumablePlayerNativeStage.Verification,
            1,
            static (first, second) => second.Queued == first.Queued + 1,
            "The exact consumable entered the native preparation queue.");
    }

    private ConsumablePlayerSubmission PlayerCancel(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var usages = native.GetUsages(item);
        var selected = native.GetNextUsage(item);
        if (selected is null)
        {
            for (var index = 0; index < usages.Count; index++)
            {
                var candidate = usages[index];
                if (candidate is not null && candidate.GetType() == native.UsageType &&
                    !native.UsageEngaged(candidate))
                {
                    selected = candidate;
                    break;
                }
            }
        }
        if (selected is null || selected.GetType() != native.UsageType)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.NoCancellableUsage,
                "The exact consumable has no native pending usage to cancel.");
        var selectedId = native.UsageGuid(selected);
        var resultInfo = native.UsageResultInfo(selected);
        if (selectedId == Guid.Empty || resultInfo is null)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.NoCancellableUsage,
                "The native pending usage has no stable identity or EffectResultInfo owner.");
        var before = Capture(item, native);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        try
        {
            native.CancelUsage(item);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ObserveCancelAfterThrow(
                in action,
                item,
                native,
                selectedId,
                resultInfo,
                in before,
                "CancelUsage threw: " + ex.GetBaseException().Message);
        }
        return VerifyCancel(
            in action,
            item,
            native,
            selectedId,
            resultInfo,
            in before,
            "The exact pending usage was cancelled and removed.");
    }

    private ConsumablePlayerSubmission PlayerDiscard(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var before = Capture(item, native);
        if (before.Amount <= 0)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.NothingToDiscard,
                "The exact consumable has no owned amount to discard.");
        var discarded = Math.Min(action.Amount, before.Amount);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        try
        {
            native.Discard(item, discarded);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ObserveAfterThrow(
                in action,
                item,
                native,
                in before,
                ConsumablePlayerNativeStage.Discard,
                1,
                (first, second) => second.Amount == first.Amount - discarded,
                "Discard threw: " + ex.GetBaseException().Message);
        }
        return Verify(
            in action,
            item,
            native,
            in before,
            ConsumablePlayerNativeStage.Verification,
            1,
            (first, second) => second.Amount == first.Amount - discarded,
            "The requested clamped amount left the exact consumable holding.");
    }

    private ConsumablePlayerSubmission PlayerSetRandomization(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.CanBeRandomized(item))
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.RandomizationUnavailable,
                "ConsumableSO.canBeRandomized is false for the exact UUID-resolved item.");
        var before = Capture(item, native);
        var requested = action.Randomized;
        if (before.Randomized == requested)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.AlreadyInRequestedState,
                "The consumable already has the requested randomization state.");
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        try
        {
            native.SetRandomization(item, requested);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ObserveAfterThrow(
                in action,
                item,
                native,
                in before,
                ConsumablePlayerNativeStage.Randomization,
                1,
                (_, second) => second.Randomized == requested,
                "SetRandomization threw: " + ex.GetBaseException().Message);
        }
        return Verify(
            in action,
            item,
            native,
            in before,
            ConsumablePlayerNativeStage.Verification,
            1,
            (_, second) => second.Randomized == requested,
            "The exact consumable now has the requested randomization state.");
    }

    private ConsumablePlayerSubmission PlayerMove(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var inventory = native.GetInventory();
        if (inventory is null)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.ListUnavailable,
                "Inventory._instance is unavailable in the current lifecycle.");
        var list = action.List == ConsumablePlayerListKind.Hotbar
            ? native.GetHotbarList(inventory)
            : native.GetInventoryList(inventory);
        if (list is null || list.GetType() != native.ListType)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.ListUnavailable,
                "Inventory did not expose the requested exact ConsumableRefListVariable.");
        var values = native.GetListValues(list);
        var source = -1;
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index], item)) continue;
            if (source >= 0)
                return ConsumablePlayerSubmission.Reject(
                    in action,
                    ConsumablePlayerPreflight.SourceUnavailable,
                    "The exact consumable appears more than once in the requested list.");
            source = index;
        }
        if (source < 0)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.SourceUnavailable,
                "The exact consumable is absent from the requested list.");
        if (action.Destination >= values.Count)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.DestinationOutOfRange,
                "Destination " + action.Destination +
                " is outside the live list length " + values.Count + ".");
        if (source == action.Destination)
            return ConsumablePlayerSubmission.Reject(
                in action,
                ConsumablePlayerPreflight.AlreadyInRequestedState,
                "The exact consumable already occupies the requested destination.");
        var before = Capture(item, native, values);
        var expected = (Guid[])before.OrderedList.Clone();
        (expected[source], expected[action.Destination]) =
            (expected[action.Destination], expected[source]);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        var calls = action.List == ConsumablePlayerListKind.Hotbar ? 3 : 2;
        try
        {
            native.Swap(list, source, action.Destination);
            native.Update(list);
            if (action.List == ConsumablePlayerListKind.Hotbar)
                native.SetAt(list, action.Destination, item);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ObserveMoveAfterThrow(
                in action,
                item,
                native,
                values,
                in before,
                expected,
                calls,
                "The native reorder pipeline threw: " + ex.GetBaseException().Message);
        }
        var after = Capture(item, native, values);
        return SequenceEqual(after.OrderedList, expected)
            ? Verified(in action, in before, in after, calls,
                "The exact consumable moved to the requested list position.")
            : Quarantine(
                in action,
                ConsumablePlayerPreflight.VerificationFailed,
                ConsumablePlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                in before,
                in after,
                calls,
                "The requested same-list order did not become observable.");
    }

    private bool TryPlayerPermit(
        in ConsumablePlayerAction action,
        out ConsumablePlayerSubmission failure)
    {
        if (TryCaptureMutationPermit(out var reason))
        {
            failure = default;
            return true;
        }
        failure = ConsumablePlayerSubmission.Reject(
            in action,
            ConsumablePlayerPreflight.MutationPermitUnavailable,
            reason);
        return false;
    }

    private ConsumablePlayerSubmission Verify(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native,
        in ConsumablePlayerState before,
        ConsumablePlayerNativeStage stage,
        int calls,
        Func<ConsumablePlayerState, ConsumablePlayerState, bool> postcondition,
        string reason)
    {
        var after = Capture(item, native);
        return postcondition(before, after)
            ? Verified(in action, in before, in after, calls, reason)
            : Quarantine(
                in action,
                ConsumablePlayerPreflight.VerificationFailed,
                stage,
                NativeMutationOutcome.PostconditionFailed,
                in before,
                in after,
                calls,
                "The exact requested consumable transition did not become observable.");
    }

    private ConsumablePlayerSubmission ObserveAfterThrow(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native,
        in ConsumablePlayerState before,
        ConsumablePlayerNativeStage stage,
        int calls,
        Func<ConsumablePlayerState, ConsumablePlayerState, bool> postcondition,
        string reason)
    {
        var after = Capture(item, native);
        return postcondition(before, after)
            ? Verified(in action, in before, in after, calls,
                reason + " The requested outcome was nevertheless observable.")
            : Quarantine(
                in action,
                ConsumablePlayerPreflight.PostCommitFault,
                stage,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                calls,
                reason);
    }

    private ConsumablePlayerSubmission VerifyCancel(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native,
        Guid usageId,
        object resultInfo,
        in ConsumablePlayerState before,
        string reason)
    {
        var after = Capture(item, native);
        var removed = !Contains(after.UsageIds, usageId);
        return removed && after.Queued == before.Queued - 1 && native.IsCancelled(resultInfo)
            ? Verified(in action, in before, in after, 1, reason)
            : Quarantine(
                in action,
                ConsumablePlayerPreflight.VerificationFailed,
                ConsumablePlayerNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed,
                in before,
                in after,
                1,
                "CancelUsage did not cancel and remove the exact selected usage.");
    }

    private ConsumablePlayerSubmission ObserveCancelAfterThrow(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native,
        Guid usageId,
        object resultInfo,
        in ConsumablePlayerState before,
        string reason)
    {
        var after = Capture(item, native);
        if (!Contains(after.UsageIds, usageId) &&
            after.Queued == before.Queued - 1 &&
            native.IsCancelled(resultInfo))
            return Verified(
                in action,
                in before,
                in after,
                1,
                reason + " The requested cancellation was nevertheless observable.");
        return Quarantine(
            in action,
            ConsumablePlayerPreflight.PostCommitFault,
            ConsumablePlayerNativeStage.Cancel,
            NativeMutationOutcome.ExecutionThrew,
            in before,
            in after,
            1,
            reason);
    }

    private ConsumablePlayerSubmission ObserveMoveAfterThrow(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native,
        IList values,
        in ConsumablePlayerState before,
        Guid[] expected,
        int calls,
        string reason)
    {
        var after = Capture(item, native, values);
        return SequenceEqual(after.OrderedList, expected)
            ? Verified(
                in action,
                in before,
                in after,
                calls,
                reason + " The requested order was nevertheless observable.")
            : Quarantine(
                in action,
                ConsumablePlayerPreflight.PostCommitFault,
                ConsumablePlayerNativeStage.Reorder,
                NativeMutationOutcome.ExecutionThrew,
                in before,
                in after,
                calls,
                reason);
    }

    private ConsumablePlayerSubmission Verified(
        in ConsumablePlayerAction action,
        in ConsumablePlayerState before,
        in ConsumablePlayerState after,
        int calls,
        string reason)
    {
        var evidence = new ConsumablePlayerEvidence(true, in before, in after);
        return new ConsumablePlayerSubmission(
            action.Kind,
            action.ConsumableId,
            ConsumablePlayerPreflight.Proceeded,
            ConsumablePlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(calls, 1, 1),
            in evidence,
            reason);
    }

    private ConsumablePlayerSubmission Quarantine(
        in ConsumablePlayerAction action,
        ConsumablePlayerPreflight preflight,
        ConsumablePlayerNativeStage stage,
        NativeMutationOutcome outcome,
        in ConsumablePlayerState before,
        in ConsumablePlayerState after,
        int calls,
        string reason)
    {
        _quarantineReason =
            "Consumable actions are quarantined for this lifecycle after " + action.Kind +
            " could not prove the exact requested outcome: " + reason;
        var evidence = new ConsumablePlayerEvidence(true, in before, in after);
        return new ConsumablePlayerSubmission(
            action.Kind,
            action.ConsumableId,
            preflight,
            stage,
            outcome,
            new NativeMutationCallOutcome(calls, 1, 0),
            in evidence,
            _quarantineReason);
    }

    private static ConsumablePlayerState Capture(
        object item,
        ConsumablePlayerNativeBindings native,
        IList? ordered = null)
    {
        var usages = native.GetUsages(item);
        var usageIds = new List<Guid>(usages.Count);
        for (var index = 0; index < usages.Count; index++)
        {
            var usage = usages[index];
            if (usage is null || usage.GetType() != native.UsageType)
                throw new InvalidOperationException(
                    "ConsumableSO.consumableUsages held an unexpected native value.");
            usageIds.Add(native.UsageGuid(usage));
        }
        var order = ordered is null ? Array.Empty<Guid>() : ReadOrder(ordered, native);
        return new ConsumablePlayerState(
            native.GetQuantity(item),
            native.GetQueued(item),
            native.IsRandomized(item),
            usageIds.ToArray(),
            order);
    }

    private static Guid[] ReadOrder(IList values, ConsumablePlayerNativeBindings native)
    {
        var result = new Guid[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null) continue;
            if (value.GetType() != native.ConsumableType)
                throw new InvalidOperationException(
                    "The consumable list held an unexpected native value at " + index + ".");
            result[index] = native.GetGuid(value);
        }
        return result;
    }

    private static bool Contains(Guid[] values, Guid expected)
    {
        for (var index = 0; index < values.Length; index++)
            if (values[index] == expected) return true;
        return false;
    }

    private static bool SequenceEqual(Guid[] left, Guid[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static bool Expected(Exception ex) =>
        ex is not StackOverflowException and
        not OutOfMemoryException and
        not AccessViolationException;
}
