using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal static class AutoItemsTemporaryItemPolicy
{
    internal static HashSet<Guid> ParseAllowlist(string csv)
    {
        var result = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var token in csv.Split(','))
        {
            if (Guid.TryParse(token.Trim(), out var id) && id != Guid.Empty)
                result.Add(id);
        }
        return result;
    }

    internal static bool IsAllowed(string csv, Guid itemId) =>
        itemId != Guid.Empty && ParseAllowlist(csv).Contains(itemId);

    internal static bool IsFamilyEnabled(
        AutoItemsConfiguration configuration,
        AutoItemsConsumableFamily family) =>
        family switch
        {
            AutoItemsConsumableFamily.Fruit => configuration.UseFruits,
            AutoItemsConsumableFamily.Potion => configuration.UsePotions,
            _ => false,
        };
}
