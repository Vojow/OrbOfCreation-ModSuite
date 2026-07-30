using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Owns the semantic Auto Scribe role-list format shared by configuration UI and worker policy.
/// A missing value means every audited producible role; <c>none</c> means no role.
/// </summary>
internal static class AutoScribeRoleSelection
{
    internal const string NoneValue = "none";

    internal static HashSet<ScrollRoleKey> ParseKnown(
        string serialized,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        var selected = new HashSet<ScrollRoleKey>();
        if (string.IsNullOrWhiteSpace(serialized))
        {
            for (var index = 0; index < roles.Count; index++)
                if (roles[index].IsProducible) selected.Add(roles[index].Key);
            return selected;
        }
        if (string.Equals(
                serialized.Trim(),
                NoneValue,
                StringComparison.OrdinalIgnoreCase))
        {
            return selected;
        }

        foreach (var entry in serialized.Split(','))
        {
            var normalized = entry.Trim();
            for (var index = 0; index < roles.Count; index++)
            {
                var role = roles[index];
                if (!role.IsProducible ||
                    !string.Equals(
                        role.Key.Value,
                        normalized,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                selected.Add(role.Key);
                break;
            }
        }
        return selected;
    }

    /// <summary>
    /// Builds the worker-owned immutable selection once per configuration generation. Null is the
    /// compact representation for the default all-roles policy.
    /// </summary>
    internal static PublicationTable<ScrollRoleKey>? ParsePublication(
        string serialized,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (string.IsNullOrWhiteSpace(serialized)) return null;
        var parsed = ParseKnown(serialized, roles);
        if (parsed.Count == 0) return PublicationTable<ScrollRoleKey>.Empty;
        var rows = new ScrollRoleKey[parsed.Count];
        parsed.CopyTo(rows);
        Array.Sort(rows);
        return PublicationTable<ScrollRoleKey>.Create(rows, rows.Length);
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
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
    }

    internal static string Serialize(
        HashSet<ScrollRoleKey> selected,
        PublicationTable<AutoScribeRoleDescriptor> roles)
    {
        if (selected is null) throw new ArgumentNullException(nameof(selected));
        if (roles is null) throw new ArgumentNullException(nameof(roles));
        if (selected.Count == 0) return NoneValue;

        var producibleCount = 0;
        var containsAll = true;
        for (var index = 0; index < roles.Count; index++)
        {
            if (!roles[index].IsProducible) continue;
            producibleCount++;
            containsAll &= selected.Contains(roles[index].Key);
        }
        if (selected.Count == producibleCount && containsAll) return string.Empty;

        var ordered = new List<ScrollRoleKey>(selected);
        ordered.Sort();
        return string.Join(",", ordered.ConvertAll(role => role.Value));
    }
}
