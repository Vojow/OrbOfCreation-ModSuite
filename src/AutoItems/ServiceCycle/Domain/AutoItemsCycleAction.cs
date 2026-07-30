using System;

namespace OrbAutomata;

internal readonly struct AutoItemsCycleAction
{
    internal AutoItemsCycleAction(
        Guid itemId,
        AutoItemsConsumableFamily family,
        long collectedAtFrame,
        long collectedAtEpoch,
        int plannedLevel = 0,
        int requestedQuantity = 1)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("An Auto Items action requires an item identity.", nameof(itemId));
        if (family is AutoItemsConsumableFamily.Unknown)
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported item family is required.");
        if (requestedQuantity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity), requestedQuantity,
                "An Auto Items action requires at least one item.");
        ItemId = itemId;
        Family = family;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
        PlannedLevel = plannedLevel;
        RequestedQuantity = requestedQuantity;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
    internal int PlannedLevel { get; }
    internal int RequestedQuantity { get; }
}
