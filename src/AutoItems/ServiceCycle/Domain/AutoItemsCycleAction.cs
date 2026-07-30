using System;

namespace OrbAutomata;

/// <summary>One exact, immutable consumable-use plan carried to the game boundary.</summary>
internal readonly struct AutoItemsCycleAction
{
    internal AutoItemsCycleAction(
        Guid itemId,
        AutoItemsConsumableFamily family,
        long collectedAtEpoch,
        int plannedLevel)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("An Auto Items action requires an item identity.", nameof(itemId));
        if (family is not (AutoItemsConsumableFamily.Relic or AutoItemsConsumableFamily.Scroll))
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported item family is required.");
        if (family == AutoItemsConsumableFamily.Scroll && plannedLevel < 1)
            throw new ArgumentOutOfRangeException(
                nameof(plannedLevel),
                "A Scroll action requires the strongest owned level from its world reading.");
        ItemId = itemId;
        Family = family;
        CollectedAtEpoch = collectedAtEpoch;
        PlannedLevel = plannedLevel;
    }

    internal Guid ItemId { get; }
    internal AutoItemsConsumableFamily Family { get; }
    internal long CollectedAtEpoch { get; }
    internal int PlannedLevel { get; }
}
