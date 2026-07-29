using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The four native consumable families Auto Items recognizes by exact stable identity.</summary>
internal enum AutoItemsConsumableFamily
{
    Unknown = 0,
    Fruit = 1,
    Potion = 2,
    Relic = 3,
    Scroll = 4,
}

/// <summary>
/// Owns the stable native taxonomy used by world policy, live revalidation, and configuration UI.
/// Names are deliberately excluded: family identity is always an audited UUID.
/// </summary>
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
        return AutoItemsConsumableFamily.Unknown;
    }

    internal static bool IsTemporary(AutoItemsConsumableFamily family) =>
        family is AutoItemsConsumableFamily.Fruit or AutoItemsConsumableFamily.Potion;
}
