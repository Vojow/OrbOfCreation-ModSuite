using System;

namespace OrbAutomata;

/// <summary>One exact, immutable consumable-use plan carried to the game boundary.</summary>
internal readonly struct AutoItemsCycleAction
{
    internal AutoItemsCycleAction(
        Guid itemId,
        AutoItemsConsumableFamily family,
        long collectedAtEpoch,
        int plannedLevel,
        long collectedAtFrame = 0)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("An Auto Items action requires an item identity.", nameof(itemId));
        if (family == AutoItemsConsumableFamily.Unknown)
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported item family is required.");
        if (!AutoItemsConsumableFamilies.IsTemporary(family) && plannedLevel < 1)
            throw new ArgumentOutOfRangeException(
                nameof(plannedLevel),
                "A permanent consumable action requires the strongest owned level from its world reading.");
        ItemId = itemId;
        Family = family;
        CollectedAtFrame = collectedAtFrame;
        CollectedAtEpoch = collectedAtEpoch;
        PlannedLevel = plannedLevel;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal long CollectedAtFrame { get; }
    internal long CollectedAtEpoch { get; }
    internal int PlannedLevel { get; }
}
