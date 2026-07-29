using System;

namespace OrbAutomata;

internal readonly struct AutoItemsPlanBelief
{
    internal AutoItemsPlanBelief(
        int quantity,
        int queuedQuantity,
        bool randomized,
        bool canBeRandomized)
    {
        Quantity = quantity;
        QueuedQuantity = queuedQuantity;
        Randomized = randomized;
        CanBeRandomized = canBeRandomized;
    }

    internal int Quantity { get; }
    internal int QueuedQuantity { get; }
    internal bool Randomized { get; }
    internal bool CanBeRandomized { get; }
}

internal readonly struct AutoItemsCycleAction
{
    internal AutoItemsCycleAction(
        Guid itemId,
        AutoItemsConsumableFamily family,
        long collectedAtFrame,
        long collectedAtEpoch,
        in AutoItemsPlanBelief belief)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("An Auto Items action requires an item identity.", nameof(itemId));
        if (family is AutoItemsConsumableFamily.Unknown)
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported item family is required.");
        ItemId = itemId;
        Family = family;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
        Belief = belief;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
    internal AutoItemsPlanBelief Belief { get; }
}
