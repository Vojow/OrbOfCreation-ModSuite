using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The exact supported native consumable families.</summary>
internal enum AutoItemsConsumableFamily
{
    Unknown = 0,
    Relic = 1,
    Scroll = 2,
    Fruit = 3,
    Potion = 4,
    Thread = 5,
}

internal static class AutoItemsConsumableFamilies
{
    internal static AutoItemsConsumableFamily FromTypeId(Guid typeId)
    {
        if (typeId == KnownEntities.ConsumableFruitType.Uuid)
            return AutoItemsConsumableFamily.Fruit;
        if (typeId == KnownEntities.ConsumablePotionType.Uuid)
            return AutoItemsConsumableFamily.Potion;
        if (typeId == KnownEntities.ConsumableRelicType.Uuid)
            return AutoItemsConsumableFamily.Relic;
        if (typeId == KnownEntities.ConsumableScrollType.Uuid)
            return AutoItemsConsumableFamily.Scroll;
        if (typeId == KnownEntities.ConsumableThreadType.Uuid)
            return AutoItemsConsumableFamily.Thread;
        return AutoItemsConsumableFamily.Unknown;
    }

    internal static bool IsTemporary(AutoItemsConsumableFamily family) =>
        family is AutoItemsConsumableFamily.Fruit or
            AutoItemsConsumableFamily.Potion or
            AutoItemsConsumableFamily.Thread;
}
