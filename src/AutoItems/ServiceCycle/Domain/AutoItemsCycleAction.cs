using System;

namespace OrbAutomata;

internal readonly struct AutoItemsCycleAction
{
    internal AutoItemsCycleAction(
        Guid itemId,
        AutoItemsConsumableFamily family,
        long collectedAtFrame,
        long collectedAtEpoch)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("An Auto Items action requires an item identity.", nameof(itemId));
        if (family is AutoItemsConsumableFamily.Unknown)
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported item family is required.");
        ItemId = itemId;
        Family = family;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
}
