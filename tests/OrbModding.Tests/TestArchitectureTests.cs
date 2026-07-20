using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace OrbModding.Tests;

public sealed class TestArchitectureTests
{
    private static readonly Regex InlineMarkdownLink = new(
        @"!?\[[^\]]*\]\((?<target>[^)\r\n]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReferenceMarkdownLink = new(
        @"^\s*\[[^\]]+\]:\s*(?<target>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex TestClassDeclaration = new(
        @"\bclass\s+(?<name>[A-Za-z][A-Za-z0-9]*Tests)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DocumentedTestOwner = new(
        @"`(?<name>[A-Za-z][A-Za-z0-9]*Tests)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void PortableCiPartitionMarkers_AreMutuallyExclusive()
    {
        var conflicts = new List<string>();
        var assembly = typeof(TestArchitectureTests).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            var classCategories = Categories(type);
            foreach (var method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance |
                         BindingFlags.Static))
            {
                if (!method.CustomAttributes.Any(attribute =>
                        typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType)))
                {
                    continue;
                }

                var categories = new HashSet<string>(classCategories, StringComparer.Ordinal);
                categories.UnionWith(Categories(method));
                if (categories.Contains("PerformanceSimulation") &&
                    categories.Contains("ExternalProcess"))
                {
                    conflicts.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.True(conflicts.Count == 0,
            "Portable tests cannot belong to both exclusive CI partitions: " +
            string.Join(", ", conflicts));
    }

    [Fact]
    public void DocumentationLocalLinksResolveInsideRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var failures = new List<string>();
        foreach (var markdownPath in MarkdownFiles(repositoryRoot))
        {
            var content = WithoutFencedCode(File.ReadAllText(markdownPath));
            var targets = InlineMarkdownLink.Matches(content)
                .Concat(ReferenceMarkdownLink.Matches(content))
                .Select(match => match.Groups["target"].Value);
            foreach (var rawTarget in targets)
            {
                if (!TryResolveLocalTarget(
                        repositoryRoot,
                        markdownPath,
                        rawTarget,
                        out var resolved,
                        out var reason))
                {
                    if (!string.IsNullOrEmpty(reason))
                    {
                        failures.Add($"{Relative(repositoryRoot, markdownPath)} -> {rawTarget}: {reason}");
                    }

                    continue;
                }

                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    failures.Add(
                        $"{Relative(repositoryRoot, markdownPath)} -> {rawTarget}: " +
                        $"missing {Relative(repositoryRoot, resolved)}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Documentation contains unresolved local links:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void DocumentationIsValidUtf8WithoutKnownMojibake()
    {
        var repositoryRoot = FindRepositoryRoot();
        var strictUtf8 = new UTF8Encoding(false, true);
        var mojibakeFragments = new[]
        {
            "\u00c2\u00b7", // corrupted middle dot
            "\u00e2\u20ac", // corrupted smart punctuation
            "\u00e2\u2020", // corrupted arrow
            "\u0102\u2014", // corrupted multiplication sign under a legacy code page
            "\u00c2\u00b2", // corrupted superscript
        };
        var failures = new List<string>();
        foreach (var markdownPath in MarkdownFiles(repositoryRoot))
        {
            string content;
            try
            {
                content = strictUtf8.GetString(File.ReadAllBytes(markdownPath));
            }
            catch (DecoderFallbackException exception)
            {
                failures.Add(
                    $"{Relative(repositoryRoot, markdownPath)} is not valid UTF-8: {exception.Message}");
                continue;
            }

            foreach (var fragment in mojibakeFragments)
            {
                if (content.Contains(fragment, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{Relative(repositoryRoot, markdownPath)} contains mojibake fragment {Escape(fragment)}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Documentation encoding checks failed:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void DocumentedTestOwnersResolveToTestClasses()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var declaredTests = Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .SelectMany(path => TestClassDeclaration.Matches(File.ReadAllText(path)))
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var markdownPath in MarkdownFiles(repositoryRoot))
        {
            var content = WithoutFencedCode(File.ReadAllText(markdownPath));
            foreach (Match match in DocumentedTestOwner.Matches(content))
            {
                var name = match.Groups["name"].Value;
                if (!declaredTests.Contains(name))
                {
                    failures.Add($"{Relative(repositoryRoot, markdownPath)} references missing test class {name}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Documentation contains stale test-owner references:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<string> Categories(MemberInfo member) =>
        member.CustomAttributes
            .Where(attribute =>
                attribute.AttributeType == typeof(TraitAttribute) &&
                attribute.ConstructorArguments.Count == 2 &&
                string.Equals(
                    attribute.ConstructorArguments[0].Value as string,
                    "Category",
                    StringComparison.Ordinal))
            .Select(attribute => attribute.ConstructorArguments[1].Value as string)
            .Where(value => value != null)
            .Select(value => value!);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")) &&
                (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                 File.Exists(Path.Combine(current.FullName, ".git"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}.");
    }

    private static IEnumerable<string> MarkdownFiles(string repositoryRoot) =>
        Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "docs"),
            "*.md",
            SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path));

    private static bool IsGeneratedPath(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));

    private static string WithoutFencedCode(string content)
    {
        var builder = new StringBuilder(content.Length);
        var fenced = false;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static bool TryResolveLocalTarget(
        string repositoryRoot,
        string markdownPath,
        string rawTarget,
        out string resolved,
        out string reason)
    {
        resolved = string.Empty;
        reason = string.Empty;
        var target = rawTarget.Trim();
        if (target.StartsWith('<') && target.EndsWith('>'))
        {
            target = target[1..^1];
        }
        else
        {
            var titleStart = target.IndexOf(" \"", StringComparison.Ordinal);
            if (titleStart >= 0)
            {
                target = target[..titleStart];
            }
        }

        if (target.Length == 0 ||
            target.StartsWith('#') ||
            Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return false;
        }

        var fragment = target.IndexOf('#');
        if (fragment >= 0)
        {
            target = target[..fragment];
        }

        var query = target.IndexOf('?');
        if (query >= 0)
        {
            target = target[..query];
        }

        if (target.Length == 0)
        {
            return false;
        }

        target = Uri.UnescapeDataString(target);
        resolved = Path.GetFullPath(
            target.StartsWith('/')
                ? Path.Combine(repositoryRoot, target.TrimStart('/'))
                : Path.Combine(Path.GetDirectoryName(markdownPath)!, target));
        var relative = Path.GetRelativePath(repositoryRoot, resolved);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            reason = $"target escapes repository ({resolved})";
            return false;
        }

        return true;
    }

    private static string Relative(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Escape(string value) =>
        string.Concat(value.Select(character => $"U+{(int)character:X4} ")).TrimEnd();
}
