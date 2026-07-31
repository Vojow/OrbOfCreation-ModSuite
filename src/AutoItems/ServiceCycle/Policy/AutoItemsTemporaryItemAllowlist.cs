using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Parses exact temporary-item UUID approval once per committed configuration publication.
/// Family membership never implies approval.
/// </summary>
internal static class AutoItemsTemporaryItemAllowlist
{
    internal static PublicationTable<Guid> ParsePublication(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return PublicationTable<Guid>.Empty;

        var parsed = new HashSet<Guid>();
        foreach (var token in csv.Split(','))
        {
            if (Guid.TryParse(token.Trim(), out var itemId) && itemId != Guid.Empty)
                parsed.Add(itemId);
        }
        if (parsed.Count == 0) return PublicationTable<Guid>.Empty;

        var rows = new Guid[parsed.Count];
        parsed.CopyTo(rows);
        Array.Sort(rows);
        return PublicationTable<Guid>.Create(rows, rows.Length);
    }

    internal static bool Contains(PublicationTable<Guid>? values, Guid itemId)
    {
        if (values is null || itemId == Guid.Empty) return false;
        var rows = values.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var comparison = rows[middle].CompareTo(itemId);
            if (comparison == 0) return true;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
    }

    internal static bool Contains(string csv, Guid itemId)
    {
        if (itemId == Guid.Empty || string.IsNullOrWhiteSpace(csv)) return false;

        var start = 0;
        while (start <= csv.Length)
        {
            var separator = csv.IndexOf(',', start);
            var stop = separator >= 0 ? separator : csv.Length;
            while (start < stop && char.IsWhiteSpace(csv[start])) start++;
            while (stop > start && char.IsWhiteSpace(csv[stop - 1])) stop--;
            if (Guid.TryParse(csv.AsSpan(start, stop - start), out var parsed) &&
                parsed == itemId)
            {
                return true;
            }
            if (separator < 0) return false;
            start = separator + 1;
        }
        return false;
    }

    internal static bool HasAnyValidEntry(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return false;
        var start = 0;
        while (start <= csv.Length)
        {
            var separator = csv.IndexOf(',', start);
            var stop = separator >= 0 ? separator : csv.Length;
            while (start < stop && char.IsWhiteSpace(csv[start])) start++;
            while (stop > start && char.IsWhiteSpace(csv[stop - 1])) stop--;
            if (Guid.TryParse(csv.AsSpan(start, stop - start), out var parsed) &&
                parsed != Guid.Empty)
            {
                return true;
            }
            if (separator < 0) return false;
            start = separator + 1;
        }
        return false;
    }
}
