using System;
using System.Collections.Generic;

namespace OrbAutomata;

internal static class AutoItemsTemporaryItemAllowlist
{
    internal static HashSet<Guid> Parse(string csv)
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
}
