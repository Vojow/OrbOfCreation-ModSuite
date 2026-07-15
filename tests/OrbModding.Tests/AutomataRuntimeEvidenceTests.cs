using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataRuntimeEvidenceTests
{
    private static readonly Regex PurchasePattern = new Regex(
        @"Auto Buy purchased one (?<kind>Structure|Upgrade) level: (?<name>.+) \((?<uuid>[0-9a-f-]+)\); SessionPurchases=(?<count>\d+)\.",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CpuLimitedBatchPattern = new Regex(
        @"Auto Buy batch complete: Purchased=(?<purchased>\d+), Attempted=(?<attempted>\d+), Eligible=(?<eligible>\d+), QueueLimited=(?<queue>True|False), CpuLimited=(?<cpu>True|False)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("upgrade-10-v0.1.3.log", "Upgrade", "Imbue Dimensional", "6489c59d-306d-4085-803a-49e09fdc5099", 409)]
    [InlineData("structure-10-v0.1.3.log", "Structure", "Concentration", "bf4e596c-3ee0-4194-b0c2-d4a7af1a85f6", 180)]
    public void EnduranceFixture_ProvesSequentialPurchasesAndTerminalStop(
        string fixtureName,
        string expectedKind,
        string expectedName,
        string expectedUuid,
        int expectedCandidateCount)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "automata", fixtureName));

        Assert.Contains(lines, line => line.Contains("Loading [Orb Automata 0.1.3]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("AutoBuySessionPurchaseLimit=10", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("ResearchMode=Disabled", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"Scanned={expectedCandidateCount}", StringComparison.Ordinal));

        var purchases = lines
            .Select(line => PurchasePattern.Match(line))
            .Where(match => match.Success)
            .ToArray();
        Assert.Equal(10, purchases.Length);
        Assert.Equal(Enumerable.Range(1, 10), purchases.Select(match => int.Parse(match.Groups["count"].Value)));
        Assert.All(purchases, match => Assert.Equal(expectedKind, match.Groups["kind"].Value));
        Assert.All(purchases, match => Assert.Equal(expectedName, match.Groups["name"].Value));
        Assert.All(purchases, match => Assert.Equal(expectedUuid, match.Groups["uuid"].Value));

        var limitIndex = Array.FindIndex(lines, line => line.Contains(
            "per-session purchase limit (10); no further purchases or candidate scans",
            StringComparison.Ordinal));
        Assert.True(limitIndex > 0, "The terminal session-limit record is missing.");
        Assert.DoesNotContain(lines.Skip(limitIndex + 1), line => line.Contains("Auto Buy", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line =>
            line.Contains("[Warning:Orb Automata]", StringComparison.Ordinal) ||
            line.Contains("[Error  :Orb Automata]", StringComparison.Ordinal) ||
            line.Contains("Exception", StringComparison.Ordinal) ||
            line.Contains("could not restore", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("could not purchase", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Version014Fixture_ProvesCpuBudgetCollapsedEveryConfiguredBatchToOnePurchase()
    {
        var lines = File.ReadAllLines(Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "automata",
            "cpu-limited-batches-v0.1.4.log"));

        Assert.Contains(lines, line => line.Contains("Loading [Orb Automata 0.1.4]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("AutoBuyBatchSize=8", StringComparison.Ordinal));

        var batches = lines
            .Select(line => CpuLimitedBatchPattern.Match(line))
            .Where(match => match.Success)
            .ToArray();
        Assert.Equal(3, batches.Length);
        Assert.All(batches, match => Assert.Equal("1", match.Groups["purchased"].Value));
        Assert.All(batches, match => Assert.Equal("1", match.Groups["attempted"].Value));
        Assert.All(batches, match => Assert.Equal("False", match.Groups["queue"].Value));
        Assert.All(batches, match => Assert.Equal("True", match.Groups["cpu"].Value));
        Assert.DoesNotContain(lines, line => line.Contains("BatchPurchases=2/8", StringComparison.Ordinal));
    }
}
