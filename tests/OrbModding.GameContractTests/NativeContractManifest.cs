using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbModding.GameContractTests;

internal sealed class NativeContractManifest
{
    public int SchemaVersion { get; set; }

    public string AuditedAt { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;

    public List<NativeAssemblyManifest> Assemblies { get; set; } = new();

    public List<NativeBaselineManifest> Baselines { get; set; } = new();

    public List<NativeContractEntry> Contracts { get; set; } = new();

    public NativeSourceAuditManifest SourceAudit { get; set; } = new();

    public static NativeContractManifest Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "native-contracts.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Native contract manifest was not copied to the test output.", path);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        return JsonSerializer.Deserialize<NativeContractManifest>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Native contract manifest is empty.");
    }

    public NativeAssemblyManifest RequireAssembly(string id) =>
        Assemblies.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

    public NativeBaselineManifest RequireBaseline(string id) =>
        Baselines.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
}

internal sealed class NativeAssemblyManifest
{
    public string Id { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;
}

internal sealed class NativeBaselineManifest
{
    public string Id { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string AuditedAt { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;

    public List<NativeBaselineAssemblyManifest> Assemblies { get; set; } = new();

    public NativeBaselineAssemblyManifest RequireAssembly(string id) =>
        Assemblies.Single(candidate => string.Equals(candidate.Assembly, id, StringComparison.Ordinal));
}

internal sealed class NativeBaselineAssemblyManifest
{
    public string Assembly { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string Provenance { get; set; } = string.Empty;
}

internal sealed class NativeContractEntry
{
    public string Id { get; set; } = string.Empty;

    public string Assembly { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string TypeVisibility { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? Member { get; set; }

    public string? Visibility { get; set; }

    public bool? Static { get; set; }

    public string? ValueType { get; set; }

    public string? ReturnType { get; set; }

    public List<string> Parameters { get; set; } = new();

    public string? BaseType { get; set; }

    public List<string> Owners { get; set; } = new();

    public List<string> Usages { get; set; } = new();

    public string Place { get; set; } = string.Empty;

    public List<string> SourceTokens { get; set; } = new();
}

internal sealed class NativeSourceAuditManifest
{
    public List<string> Roots { get; set; } = new();

    public List<NativeSourceExemption> Exemptions { get; set; } = new();
}

internal sealed class NativeSourceExemption
{
    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

internal static class RepositoryPaths
{
    public static string RequireRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "data")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output.");
    }
}
