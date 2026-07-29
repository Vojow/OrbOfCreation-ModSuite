using System;
using System.Collections.Generic;
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
/// Bridges a verified main-thread submission to later immutable world publications. It retains
/// identities and frame numbers only; native objects never cross to the worker.
/// </summary>
internal sealed class AutoItemsTemporaryActivationTracker
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _quarantined = new();
    private Guid _pendingItem;
    private long _submittedFromFrame;
    private bool _activationSeen;

    internal void RecordSubmitted(Guid itemId, long submittedFromFrame)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("A submitted item requires an identity.", nameof(itemId));
        lock (_gate)
        {
            if (_quarantined.Contains(itemId)) return;
            _pendingItem = itemId;
            _submittedFromFrame = submittedFromFrame;
            _activationSeen = false;
        }
    }

    internal AutoItemsTemporaryActivationState Observe(
        GameWorldState world,
        out Guid itemId)
    {
        lock (_gate)
        {
            itemId = _pendingItem;
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
                return QuarantinePending(out itemId);
            if (usageCount == 1)
            {
                if (engaged)
                {
                    _activationSeen = true;
                    return AutoItemsTemporaryActivationState.Active;
                }
                return AutoItemsTemporaryActivationState.AwaitingActivation;
            }

            if (world.CollectedAtFrame <= _submittedFromFrame)
                return AutoItemsTemporaryActivationState.AwaitingActivation;
            if (WorldLookup.TryFind(world.Consumables, itemId, out var consumable) &&
                (consumable.QueuedQuantity > 0 ||
                 consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) > 0))
                return AutoItemsTemporaryActivationState.AwaitingActivation;

            if (!_activationSeen)
                return QuarantinePending(out itemId);

            _pendingItem = Guid.Empty;
            _submittedFromFrame = 0;
            _activationSeen = false;
            return AutoItemsTemporaryActivationState.Completed;
        }
    }

    internal bool IsQuarantined(Guid itemId)
    {
        lock (_gate) return _quarantined.Contains(itemId);
    }

    internal void ResetLifecycle()
    {
        lock (_gate)
        {
            _pendingItem = Guid.Empty;
            _submittedFromFrame = 0;
            _activationSeen = false;
            _quarantined.Clear();
        }
    }

    private AutoItemsTemporaryActivationState QuarantinePending(out Guid itemId)
    {
        itemId = _pendingItem;
        _quarantined.Add(itemId);
        _pendingItem = Guid.Empty;
        _submittedFromFrame = 0;
        _activationSeen = false;
        return AutoItemsTemporaryActivationState.Quarantined;
    }
}
