using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class NativeContractManifestTests
{
    private static readonly Regex QualifiedTargetPattern = new(
        "\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*):(?<member>[A-Za-z_][A-Za-z0-9_]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeResolverPattern = new(
        "(?:AccessTools\\.TypeByName|ReflectionUtil\\.FindLoadedType)\\s*\\(\\s*\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LiteralSelectorPattern = new(
        "(?:GetMethod|GetField|GetProperty|FindMethod|FindField|FindNoArgMethod|ResolveStaticNoArgMethod|InvokeNoArgs|TryInvokeBool|ReadMember|ReadBool|ReadInt|InvokeRequired|ReadStaticList)\\s*\\([^;\\r\\n]*?\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReflectionUsePattern = new(
        "(?:HarmonyPatch|AccessTools\\.(?:Method|TypeByName)|ReflectionUtil\\.FindLoadedType|\\.(?:GetMethod|GetMethods|GetField|GetFields|GetProperty|GetProperties)\\s*\\(|\\b(?:FindMethod|FindField)\\s*\\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Manifest_IsCompleteAndInternallyConsistent()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositoryPaths.RequireRoot();

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(manifest.AuditedAt));
        Assert.False(string.IsNullOrWhiteSpace(manifest.GameBuild));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Provenance));
        Assert.NotEmpty(manifest.Assemblies);
        Assert.NotEmpty(manifest.Contracts);

        Assert.All(manifest.Assemblies, assembly =>
        {
            Assert.False(string.IsNullOrWhiteSpace(assembly.Id));
            Assert.EndsWith(".dll", assembly.File, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[A-F0-9]{64}$", assembly.Sha256);
            Assert.False(string.IsNullOrWhiteSpace(assembly.Provenance));
        });
        Assert.Equal(manifest.Assemblies.Count, manifest.Assemblies.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(manifest.Contracts.Count, manifest.Contracts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manifest.Contracts, contract =>
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Id));
            Assert.Contains(manifest.Assemblies, assembly => assembly.Id == contract.Assembly);
            Assert.False(string.IsNullOrWhiteSpace(contract.Type));
            Assert.Contains(contract.TypeVisibility, new[] { "public", "private", "family", "assembly", "family-or-assembly", "family-and-assembly" });
            Assert.Contains(contract.Kind, new[] { "type", "field", "method" });
            Assert.NotEmpty(contract.Owners);
            Assert.All(contract.Owners, owner => Assert.False(string.IsNullOrWhiteSpace(owner)));
            Assert.NotEmpty(contract.Usages);
            Assert.All(contract.Usages, usage => Assert.Contains(usage, new[] { "direct", "reflection", "harmony" }));
            Assert.NotEmpty(contract.Sources);
            Assert.All(contract.Sources, source =>
            {
                Assert.False(string.IsNullOrWhiteSpace(source));
                Assert.True(
                    File.Exists(Path.Combine(repositoryRoot, source.Replace('/', Path.DirectorySeparatorChar))),
                    $"Manifest source does not exist: {source}");
            });

            if (contract.Kind == "field")
            {
                Assert.False(string.IsNullOrWhiteSpace(contract.Member));
                Assert.False(string.IsNullOrWhiteSpace(contract.Visibility));
                Assert.NotNull(contract.Static);
                Assert.False(string.IsNullOrWhiteSpace(contract.ValueType));
            }
            else if (contract.Kind == "method")
            {
                Assert.False(string.IsNullOrWhiteSpace(contract.Member));
                Assert.False(string.IsNullOrWhiteSpace(contract.Visibility));
                Assert.NotNull(contract.Static);
                Assert.False(string.IsNullOrWhiteSpace(contract.ReturnType));
            }
            else
            {
                Assert.Null(contract.Member);
            }
        });

        Assert.NotEmpty(manifest.SourceAudit.Roots);
        Assert.Equal(
            manifest.SourceAudit.Roots.Count,
            manifest.SourceAudit.Roots.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(manifest.SourceAudit.Roots, root =>
            Assert.True(
                Directory.Exists(Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar))),
                $"Source-audit root does not exist: {root}"));
        Assert.Equal(
            manifest.SourceAudit.Exemptions.Count,
            manifest.SourceAudit.Exemptions.Select(item => NormalizePath(item.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RuntimeHashGuards_MatchManifestBaseline()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Equal(
            GameAssemblyAudit.ExpectedAssemblyCSharpSha256,
            manifest.RequireAssembly("assembly-csharp").Sha256,
            ignoreCase: true);
        Assert.Equal(
            GameAssemblyAudit.ExpectedFirstPassSha256,
            manifest.RequireAssembly("assembly-csharp-firstpass").Sha256,
            ignoreCase: true);
    }

    [Fact]
    public void ActiveNativeReflectionAndHarmonyTargets_AreDeclared()
    {
        var manifest = NativeContractManifest.Load();
        var repositoryRoot = RepositoryPaths.RequireRoot();
        var exemptions = manifest.SourceAudit.Exemptions.ToDictionary(
            exemption => NormalizePath(exemption.Path),
            exemption => exemption,
            StringComparer.OrdinalIgnoreCase);
        Assert.All(exemptions.Values, exemption => Assert.False(string.IsNullOrWhiteSpace(exemption.Reason)));

        var declaredSources = manifest.Contracts
            .SelectMany(contract => contract.Sources.Select(source => (Path: NormalizePath(source), Contract: contract)))
            .ToLookup(item => item.Path, item => item.Contract, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var root in manifest.SourceAudit.Roots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root.Replace('/', Path.DirectorySeparatorChar));
            foreach (var sourcePath in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, sourcePath));
                var source = File.ReadAllText(sourcePath);
                if (!ReflectionUsePattern.IsMatch(source))
                {
                    continue;
                }

                if (exemptions.ContainsKey(relativePath))
                {
                    continue;
                }

                var sourceContracts = declaredSources[relativePath].ToArray();
                if (sourceContracts.Length == 0)
                {
                    failures.Add($"{relativePath}: reflection/Harmony source has no manifest contracts or documented exemption");
                    continue;
                }

                var candidates = FindLiteralTargets(source).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var candidate in candidates)
                {
                    if (!sourceContracts.Any(contract =>
                            contract.Type == candidate ||
                            contract.Member == candidate ||
                            contract.SourceTokens.Contains(candidate, StringComparer.Ordinal)))
                    {
                        failures.Add($"{relativePath}: native target literal '{candidate}' is not declared by a manifest contract for this source");
                    }
                }
            }
        }

        foreach (var exemption in exemptions.Values)
        {
            var sourcePath = Path.Combine(repositoryRoot, exemption.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(sourcePath), $"Source-audit exemption no longer exists: {exemption.Path}");
            Assert.Matches(ReflectionUsePattern, File.ReadAllText(sourcePath));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [GameAssemblyFact]
    public void InstalledGame_MatchesManifest()
    {
        var manifest = NativeContractManifest.Load();
        var paths = GameAssemblyPaths.Require();
        var failures = new List<string>();

        foreach (var assemblyEntry in manifest.Assemblies)
        {
            var path = Path.Combine(paths.GameRoot, "Orb Of Creation_Data", "Managed", assemblyEntry.File);
            using var stream = File.OpenRead(path);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, assemblyEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{assemblyEntry.File}: expected SHA-256 {assemblyEntry.Sha256}, actual {actualHash}");
                continue;
            }

            using var metadata = new GameAssemblyMetadata(path);
            foreach (var contract in manifest.Contracts.Where(contract => contract.Assembly == assemblyEntry.Id))
            {
                ValidateContract(metadata, contract, failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void ValidateContract(
        GameAssemblyMetadata metadata,
        NativeContractEntry expected,
        ICollection<string> failures)
    {
        try
        {
            var type = metadata.GetType(expected.Type);
            if (type.Visibility != expected.TypeVisibility)
            {
                failures.Add($"{expected.Id}: type visibility expected {expected.TypeVisibility}, actual {type.Visibility}");
            }

            if (expected.Kind == "type")
            {
                if (expected.BaseType is not null && type.BaseType != expected.BaseType)
                {
                    failures.Add($"{expected.Id}: base type expected {expected.BaseType}, actual {type.BaseType}");
                }
                return;
            }

            if (expected.Kind == "field")
            {
                var field = metadata.GetField(expected.Type, expected.Member!);
                if (field.Visibility != expected.Visibility || field.IsStatic != expected.Static || field.FieldType != expected.ValueType)
                {
                    failures.Add(
                        $"{expected.Id}: expected {expected.Visibility} {(expected.Static == true ? "static " : string.Empty)}{expected.ValueType}, " +
                        $"actual {field.Visibility} {(field.IsStatic ? "static " : string.Empty)}{field.FieldType}");
                }
                return;
            }

            var matches = metadata.GetMethods(expected.Type, expected.Member!);
            if (!matches.Any(method =>
                    method.Visibility == expected.Visibility &&
                    method.IsStatic == expected.Static &&
                    method.ReturnType == expected.ReturnType &&
                    method.ParameterTypes.SequenceEqual(expected.Parameters)))
            {
                var actual = string.Join(
                    "; ",
                    matches.Select(method =>
                        $"{method.Visibility} {(method.IsStatic ? "static " : string.Empty)}{method.ReturnType}({string.Join(",", method.ParameterTypes)})"));
                failures.Add(
                    $"{expected.Id}: expected {expected.Visibility} {(expected.Static == true ? "static " : string.Empty)}" +
                    $"{expected.ReturnType}({string.Join(",", expected.Parameters)}); actual [{actual}]");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{expected.Id}: {exception.Message}");
        }
    }

    private static IEnumerable<string> FindLiteralTargets(string source)
    {
        foreach (Match match in QualifiedTargetPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
            yield return match.Groups["member"].Value;
        }

        foreach (Match match in TypeResolverPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
        }

        foreach (Match match in LiteralSelectorPattern.Matches(source))
        {
            yield return match.Groups["target"].Value;
        }

        if (source.Contains("[HarmonyPatch]", StringComparison.Ordinal))
        {
            foreach (Match match in Regex.Matches(source, "\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\""))
            {
                yield return match.Groups["target"].Value;
            }
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
