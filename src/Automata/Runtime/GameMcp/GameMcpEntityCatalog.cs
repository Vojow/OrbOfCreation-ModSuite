#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// MCP query projection over the Common live identity catalog. This owns no names and performs no
/// native read: every result comes from the immutable catalog pinned by the answering world.
/// </summary>
internal static class GameMcpEntityCatalog
{
    internal static JObject Search(
        EntityIdentityCatalogSnapshot catalog,
        string query,
        int limit)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return NotAvailable("query_required", "query must not be empty");
        if (limit <= 0 || limit > 200)
            return NotAvailable("invalid_limit", "limit must be between 1 and 200");
        if (!catalog.IsBound)
            return NotAvailable(
                "entity_catalog_unavailable",
                catalog.FailureReason.Length > 0
                    ? catalog.FailureReason
                    : "the live entity catalog has not bound in this playing lifecycle yet");

        var matches = new JArray();
        var totalMatches = 0;
        var rows = catalog.Rows.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (!Matches(in row, normalized)) continue;
            totalMatches++;
            if (matches.Count < limit) matches.Add(Project(catalog, in row));
        }

        var result = new JObject
        {
            ["status"] = "available",
            ["total"] = totalMatches,
            ["returned"] = matches.Count,
            ["matches"] = matches,
        };
        if (totalMatches > matches.Count) result["nextOffset"] = matches.Count;
        return result;
    }

    internal static JObject Lookup(EntityIdentityCatalogSnapshot catalog, Guid uuid)
    {
        if (!catalog.IsBound)
            return NotAvailable(
                "entity_catalog_unavailable",
                catalog.FailureReason.Length > 0
                    ? catalog.FailureReason
                    : "the live entity catalog has not bound in this playing lifecycle yet");
        if (catalog.TryGet(uuid, out var row)) return Project(catalog, in row);

        var unavailable = NotAvailable(
            "entity_name_unavailable",
            "the live entity catalog has no entry for UUID " + uuid.ToString("D"));
        unavailable["uuid"] = uuid.ToString("D");
        return unavailable;
    }

    internal static bool Matches(
        EntityIdentityCatalogSnapshot catalog,
        Guid uuid,
        string query) =>
        catalog.TryGet(uuid, out var row)
            ? Matches(in row, query ?? string.Empty)
            : uuid.ToString("D").IndexOf(
                query ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool Matches(in EntityIdentityName row, string query) =>
        row.EntityId.ToString("D").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.RuntimeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.AssetName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static JObject Project(
        EntityIdentityCatalogSnapshot catalog,
        in EntityIdentityName row)
    {
        var identity = EntityIdentityFormatter.Describe(row.EntityId, catalog);
        var result = new JObject
        {
            ["uuid"] = row.EntityId.ToString("D"),
            ["nativeType"] = row.RuntimeType,
        };
        if (identity.HasName) result["name"] = identity.Name;
        if (identity.Source == EntityIdentityNameSource.LiveAssetName)
            result["nameSource"] = "asset";
        if (row.AssetName.Length > 0 &&
            !string.Equals(identity.Name, row.AssetName, StringComparison.Ordinal))
            result["internalName"] = row.AssetName;
        if (GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                row.RuntimeType,
                out var category))
            result["category"] = category;
        else result["category"] = "not-world-projected";
        if (!identity.HasName)
        {
            result["nameEvidence"] = new JObject
            {
                ["status"] = "not_available",
                ["code"] = "entity_name_unavailable",
                ["reason"] = "the live registry entry has no player-facing or asset name",
            };
        }
        return result;
    }

    private static JObject NotAvailable(string code, string reason) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["reason"] = reason,
    };
}
#endif
