using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Owns the semantic Auto Scribe role-list format. The worker parses it once per configuration
/// generation; actions deliberately carry no role key and never consult a newer configuration.
/// </summary>
internal static class AutoScribeRoleSelection
{
    internal const string NoneValue = "none";

    internal static PublicationTable<ScrollRoleKey>? ParsePublication(
        string serialized,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        if (string.Equals(serialized.Trim(), NoneValue, StringComparison.OrdinalIgnoreCase))
            return PublicationTable<ScrollRoleKey>.Empty;

        var selected = new ScrollRoleKey[roles.Count];
        var count = 0;
        foreach (var entry in serialized.Split(','))
        {
            var normalized = entry.Trim();
            for (var index = 0; index < roles.Count; index++)
            {
                var role = roles[index];
                if (!role.IsProducible ||
                    !string.Equals(role.Key.Value, normalized, StringComparison.Ordinal) ||
                    Contains(selected, count, role.Key))
                    continue;
                selected[count++] = role.Key;
                break;
            }
        }
        if (count == 0) return PublicationTable<ScrollRoleKey>.Empty;
        Array.Sort(selected, 0, count);
        return PublicationTable<ScrollRoleKey>.Create(selected, count);
    }

    internal static bool Contains(
        PublicationTable<ScrollRoleKey>? selected,
        ScrollRoleKey role)
    {
        if (selected is null) return true;
        var rows = selected.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = rows[middle].CompareTo(role);
            if (comparison == 0) return true;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }

    private static bool Contains(ScrollRoleKey[] values, int count, ScrollRoleKey value)
    {
        for (var index = 0; index < count; index++)
            if (values[index] == value) return true;
        return false;
    }
}
