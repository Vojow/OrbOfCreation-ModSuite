using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The exact native consumable families in this first Auto Items lane.</summary>
internal enum AutoItemsConsumableFamily
{
    Unknown = 0,
    Relic = 1,
    Scroll = 2,
}

internal static class AutoItemsConsumableFamilies
{
    internal static AutoItemsConsumableFamily FromTypeId(Guid typeId)
    {
        if (typeId == KnownEntities.ConsumableRelicType.Uuid)
            return AutoItemsConsumableFamily.Relic;
        if (typeId == KnownEntities.ConsumableScrollType.Uuid)
            return AutoItemsConsumableFamily.Scroll;
        return AutoItemsConsumableFamily.Unknown;
    }
}
