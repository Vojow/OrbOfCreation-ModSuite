using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OrbModding.Tests;

public sealed class KnowledgeMapTests
{
    private const string AlchemicScrollUuid = "67acd892-8a8a-455a-aa71-3fb06e75bf38";
    private const string AchievementStrengthUuid = "534d8a27-7320-4ca1-8d8c-7eaf0ade385c";

    [Fact]
    public void EntityMappings_AreUniqueValidAndMatchTypeSummary()
    {
        var mappings = ReadMappings();
        var typeSummary = ReadTypeSummary();

        Assert.Equal(2792, mappings.Count);
        Assert.Equal(141, typeSummary.Count);
        Assert.All(mappings, row => Assert.True(Guid.TryParse(row.Id, out _), $"Invalid UUID: {row.Id}"));
        Assert.Equal(mappings.Count, mappings.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var actualCounts = mappings
            .GroupBy(row => row.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(typeSummary.Count, actualCounts.Count);
        foreach (var expected in typeSummary)
        {
            Assert.True(actualCounts.TryGetValue(expected.Key, out var actual), $"Mapped type missing: {expected.Key}");
            Assert.Equal(expected.Value, actual);
        }
    }

    [Fact]
    public void KnownResourceAndGlobalIdentifiers_MatchCatalog()
    {
        var byId = ReadMappings().ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);

        AssertMapping(byId, AlchemicScrollUuid, "AlchemicScroll", "ResourceSO");
        AssertMapping(
            byId,
            AchievementStrengthUuid,
            "AchievementStrength",
            "IntVariable");
    }

    private static IReadOnlyList<EntityMapping> ReadMappings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "entity-mappings.tsv");
        var lines = File.ReadAllLines(path);
        Assert.Equal("id\tname\ttype", lines[0]);
        return lines.Skip(1).Select(line =>
        {
            var parts = line.Split('\t');
            Assert.Equal(3, parts.Length);
            return new EntityMapping(parts[0], parts[1], parts[2]);
        }).ToArray();
    }

    private static IReadOnlyDictionary<string, int> ReadTypeSummary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "entity-types.tsv");
        var lines = File.ReadAllLines(path);
        Assert.Equal("type\tcount", lines[0]);
        return lines.Skip(1).ToDictionary(
            line => line.Split('\t')[0],
            line => int.Parse(line.Split('\t')[1]),
            StringComparer.Ordinal);
    }

    private static void AssertMapping(
        IReadOnlyDictionary<string, EntityMapping> byId,
        string id,
        string name,
        string type)
    {
        Assert.True(byId.TryGetValue(id, out var mapping), $"Mapping missing: {id}");
        Assert.Equal(name, mapping.Name);
        Assert.Equal(type, mapping.Type);
    }

    private sealed record EntityMapping(string Id, string Name, string Type);
}
