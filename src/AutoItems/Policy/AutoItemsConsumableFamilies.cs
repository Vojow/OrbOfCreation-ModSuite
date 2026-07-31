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

/// <summary>
/// Exact supported memberships from one native <c>consumableTypes</c> relation. Membership is a
/// set: the accepted game data deliberately authors permanent Fruits as both Fruit and Relic.
/// </summary>
internal struct AutoItemsConsumableFamilySet
{
    private int _mask;

    internal int Count { get; private set; }

    internal bool TryAdd(AutoItemsConsumableFamily family)
    {
        if (family == AutoItemsConsumableFamily.Unknown)
            throw new ArgumentOutOfRangeException(nameof(family), family, "A supported family is required.");
        var bit = 1 << (int)family;
        if ((_mask & bit) != 0) return false;
        _mask |= bit;
        Count++;
        return true;
    }

    internal bool Contains(AutoItemsConsumableFamily family) =>
        family != AutoItemsConsumableFamily.Unknown &&
        (_mask & (1 << (int)family)) != 0;

    /// <summary>
    /// Resolve the one native operation protocol. Relic dominates Fruit only for the authored
    /// Fruit+Relic topology: those permanent Fruits have no temporary duration/usage contract.
    /// Other cross-protocol combinations remain incoherent until evidenced by accepted game data.
    /// </summary>
    internal bool TryResolveExecutionFamily(out AutoItemsConsumableFamily family)
    {
        if (Count == 1)
        {
            for (var value = (int)AutoItemsConsumableFamily.Relic;
                 value <= (int)AutoItemsConsumableFamily.Thread;
                 value++)
            {
                var candidate = (AutoItemsConsumableFamily)value;
                if (!Contains(candidate)) continue;
                family = candidate;
                return true;
            }
        }

        if (Count == 2 &&
            Contains(AutoItemsConsumableFamily.Fruit) &&
            Contains(AutoItemsConsumableFamily.Relic))
        {
            family = AutoItemsConsumableFamily.Relic;
            return true;
        }

        family = AutoItemsConsumableFamily.Unknown;
        return false;
    }

    internal string Describe()
    {
        var value = string.Empty;
        for (var numeric = (int)AutoItemsConsumableFamily.Relic;
             numeric <= (int)AutoItemsConsumableFamily.Thread;
             numeric++)
        {
            var candidate = (AutoItemsConsumableFamily)numeric;
            if (!Contains(candidate)) continue;
            if (value.Length != 0) value += ", ";
            value += candidate.ToString();
        }
        return value.Length == 0 ? "none" : value;
    }
}
