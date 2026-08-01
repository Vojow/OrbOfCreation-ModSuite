#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Complete authored identity/name catalog embedded in profiler builds. It is independent of the
/// current save and world visibility, so content which has not been revealed is still diagnosable
/// by player-facing or internal name without a direct game read.
/// </summary>
internal static class GameMcpEntityCatalog
{
    private const string IdentityResource = "OrbModSuite.GameMcp.entity-mappings.tsv";
    private const string DisplayResource = "OrbModSuite.GameMcp.entity-display-names.tsv";
    private static readonly Lazy<CatalogLoad> Loaded = new(
        Load,
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _loadCount;

    /// <summary>Decode the build-authored assets once during perf-debug startup, never on demand.</summary>
    internal static string Warm()
    {
        var loaded = Loaded.Value;
        return loaded.Failure;
    }

    internal static int LoadCount => Volatile.Read(ref _loadCount);

    internal static JObject Search(string query, int limit)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return NotAvailable("query_required", "query must not be empty");
        if (limit <= 0 || limit > 200)
            return NotAvailable("invalid_limit", "limit must be between 1 and 200");

        var loaded = Loaded.Value;
        if (loaded.Failure.Length > 0)
            return NotAvailable("entity_catalog_unavailable", loaded.Failure);

        var matches = new JArray();
        var totalMatches = 0;
        for (var index = 0; index < loaded.Rows.Length; index++)
        {
            var row = loaded.Rows[index];
            if (!row.Matches(normalized)) continue;
            totalMatches++;
            if (matches.Count < limit) matches.Add(row.Project());
        }

        var result = new JObject { ["status"] = "available" };
        if (matches.Count > 0) result["matches"] = matches;
        if (totalMatches > matches.Count) result["truncated"] = true;
        return result;
    }

    internal static bool TryGet(Guid uuid, out GameMcpEntityIdentity identity)
    {
        var loaded = Loaded.Value;
        identity = default;
        return loaded.Failure.Length == 0 && loaded.ByUuid.TryGetValue(uuid, out identity);
    }

    internal static JObject Lookup(Guid uuid)
    {
        var loaded = Loaded.Value;
        if (loaded.Failure.Length > 0)
            return NotAvailable("entity_catalog_unavailable", loaded.Failure);
        for (var index = 0; index < loaded.Rows.Length; index++)
        {
            if (loaded.Rows[index].Uuid != uuid) continue;
            var result = loaded.Rows[index].Project();
            return result;
        }
        var unavailable = NotAvailable(
            "entity_name_unavailable",
            "the complete authored catalog has no player-facing name for UUID " +
            uuid.ToString("D"));
        unavailable["uuid"] = uuid.ToString("D");
        return unavailable;
    }

    internal static bool Matches(Guid uuid, string query)
    {
        var loaded = Loaded.Value;
        if (loaded.Failure.Length > 0) return false;
        for (var index = 0; index < loaded.Rows.Length; index++)
        {
            var row = loaded.Rows[index];
            if (row.Uuid == uuid) return row.Matches(query ?? string.Empty);
        }
        return uuid.ToString("D").IndexOf(
            query ?? string.Empty,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static CatalogLoad Load()
    {
        Interlocked.Increment(ref _loadCount);
        try
        {
            var assembly = typeof(GameMcpEntityCatalog).Assembly;
            var displayNames = ReadDisplayNames(assembly);
            using var identityStream = assembly.GetManifestResourceStream(IdentityResource);
            if (identityStream is null)
                return CatalogLoad.Failed("embedded resource " + IdentityResource + " is missing");
            using var reader = new StreamReader(identityStream);
            var header = reader.ReadLine();
            if (!string.Equals(header, "id\tname\ttype", StringComparison.Ordinal))
                return CatalogLoad.Failed("entity-mappings.tsv has an unexpected header");

            var rows = new List<CatalogRow>(2_792);
            var seen = new HashSet<Guid>();
            string? line;
            var lineNumber = 1;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                if (line.Length == 0) continue;
                var cells = line.Split('\t');
                if (cells.Length != 3 ||
                    !Guid.TryParseExact(cells[0], "D", out var uuid) ||
                    uuid == Guid.Empty ||
                    cells[1].Length == 0 ||
                    cells[2].Length == 0)
                {
                    return CatalogLoad.Failed(
                        "entity-mappings.tsv line " +
                        lineNumber.ToString(CultureInfo.InvariantCulture) + " is malformed");
                }
                if (!seen.Add(uuid))
                    return CatalogLoad.Failed("entity-mappings.tsv repeats UUID " + uuid.ToString("D"));

                var displayName = string.Empty;
                if (displayNames.TryGetValue(uuid, out var display))
                {
                    if (!string.Equals(display.NativeType, cells[2], StringComparison.Ordinal) ||
                        !string.Equals(display.InternalName, cells[1], StringComparison.Ordinal))
                    {
                        return CatalogLoad.Failed(
                            "display-name identity disagrees for UUID " + uuid.ToString("D"));
                    }
                    displayName = display.DisplayName;
                }
                rows.Add(new CatalogRow(uuid, cells[2], cells[1], displayName));
            }
            if (rows.Count == 0)
                return CatalogLoad.Failed("entity-mappings.tsv contains no entity rows");
            var rowsWithDisplayName = rows.Count(row => row.DisplayName.Length > 0);
            var values = rows.ToArray();
            var byUuid = new Dictionary<Guid, GameMcpEntityIdentity>(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                var row = values[index];
                GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                    row.NativeType,
                    out var category);
                byUuid.Add(
                    row.Uuid,
                    new GameMcpEntityIdentity(
                        row.Uuid,
                        row.DisplayName.Length > 0 ? row.DisplayName : row.InternalName,
                        row.InternalName,
                        row.NativeType,
                        category));
            }
            return new CatalogLoad(values, byUuid, rowsWithDisplayName, string.Empty);
        }
        catch (Exception exception)
        {
            return CatalogLoad.Failed(
                "embedded entity catalog could not be decoded: " +
                exception.GetBaseException().Message);
        }
    }

    private static Dictionary<Guid, DisplayRow> ReadDisplayNames(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(DisplayResource) ??
            throw new InvalidOperationException("embedded resource " + DisplayResource + " is missing");
        using var reader = new StreamReader(stream);
        var header = reader.ReadLine();
        if (!string.Equals(header, "id\ttype\tname\tdisplayName", StringComparison.Ordinal))
            throw new InvalidOperationException("entity-display-names.tsv has an unexpected header");

        var result = new Dictionary<Guid, DisplayRow>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length == 0) continue;
            var cells = line.Split('\t');
            if (cells.Length != 4 || !Guid.TryParseExact(cells[0], "D", out var uuid))
            {
                throw new InvalidOperationException(
                    "entity-display-names.tsv line " +
                    lineNumber.ToString(CultureInfo.InvariantCulture) + " is malformed");
            }
            if (!result.TryAdd(uuid, new DisplayRow(cells[1], cells[2], cells[3])))
                throw new InvalidOperationException(
                    "entity-display-names.tsv repeats UUID " + uuid.ToString("D"));
        }
        return result;
    }

    private static JObject NotAvailable(string code, string reason) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["reason"] = reason,
    };

    private readonly record struct DisplayRow(
        string NativeType,
        string InternalName,
        string DisplayName);

    private readonly record struct CatalogRow(
        Guid Uuid,
        string NativeType,
        string InternalName,
        string DisplayName)
    {
        internal bool Matches(string query) =>
            Uuid.ToString("D").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            NativeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            InternalName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        internal JObject Project() => new()
        {
            ["uuid"] = Uuid.ToString("D"),
            ["nativeType"] = NativeType,
            ["name"] = DisplayName.Length > 0 ? DisplayName : InternalName,
            ["internalName"] = DisplayName.Length > 0 &&
                !string.Equals(DisplayName, InternalName, StringComparison.Ordinal)
                    ? InternalName
                    : null,
            ["category"] = GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                NativeType,
                out var category)
                    ? category
                    : null,
        };
    }

    private sealed class CatalogLoad
    {
        internal CatalogLoad(
            CatalogRow[] rows,
            Dictionary<Guid, GameMcpEntityIdentity> byUuid,
            int rowsWithDisplayName,
            string failure)
        {
            Rows = rows;
            ByUuid = byUuid;
            RowsWithDisplayName = rowsWithDisplayName;
            Failure = failure;
        }

        internal CatalogRow[] Rows { get; }
        internal Dictionary<Guid, GameMcpEntityIdentity> ByUuid { get; }
        internal int RowsWithDisplayName { get; }
        internal string Failure { get; }

        internal static CatalogLoad Failed(string reason) =>
            new(
                Array.Empty<CatalogRow>(),
                new Dictionary<Guid, GameMcpEntityIdentity>(),
                0,
                reason);
    }
}

internal readonly struct GameMcpEntityIdentity
{
    internal GameMcpEntityIdentity(
        Guid uuid,
        string name,
        string internalName,
        string nativeType,
        string category)
    {
        Uuid = uuid;
        Name = name ?? string.Empty;
        InternalName = internalName ?? string.Empty;
        NativeType = nativeType ?? string.Empty;
        Category = category ?? string.Empty;
    }

    internal Guid Uuid { get; }
    internal string Name { get; }
    internal string InternalName { get; }
    internal string NativeType { get; }
    internal string Category { get; }
}
#endif
