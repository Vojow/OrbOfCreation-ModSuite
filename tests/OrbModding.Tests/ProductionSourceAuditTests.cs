using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace OrbModding.Tests;

public sealed class ProductionSourceAuditTests
{
    [Fact]
    public void ProductionCSharpContainsNoUseGameStubsTokens()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("bin/", StringComparison.Ordinal) ||
                relativePath.StartsWith("bin-", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj/", StringComparison.Ordinal) ||
                relativePath.StartsWith("obj-", StringComparison.Ordinal))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("USE_GAME_STUBS", StringComparison.Ordinal))
                {
                    offenders.Add(relativePath + ":" + lineNumber);
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Production C# source must not vary for game stubs: " + string.Join(", ", offenders));
    }

    [Fact]
    public void GameMcpProjectionsLeaveWireCodeNormalizationToTheEncoder()
    {
        var projectionRoot = Path.Combine(
            FindRepositoryRoot(), "src", "Automata", "Runtime", "GameMcp");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     projectionRoot, "*Projection.cs", SearchOption.TopDirectoryOnly))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.Contains("GameMcpEntityWireNormalizer.Snake(", StringComparison.Ordinal))
                    offenders.Add(Path.GetFileName(path) + ":" + lineNumber);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "MCP projections must publish domain vocabulary; the one wire encoder owns code normalization: " +
            string.Join(", ", offenders));
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "OrbModSuite.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository source directory.");
    }
}
