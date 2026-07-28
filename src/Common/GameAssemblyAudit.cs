using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace OrbModding.Common;

public static class GameAssemblyAudit
{
    public const string WindowsBaselineId = "steam-windows-2026-07-11";
    public const string MacBaselineId = "steam-macos-2026-07-13";
    public const string MacV1052BaselineId = "steam-macos-2026-07-28";

    public const string WindowsAssemblyCSharpSha256 =
        "5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F";
    public const string WindowsFirstPassSha256 =
        "D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A";
    public const string MacAssemblyCSharpSha256 =
        "5652EBE35A4B87223A014EAA7B364AE921477D2E016789CB4E13C8C892055DE4";
    public const string MacFirstPassSha256 =
        "CAFE3F4FC522B3AF33A10CB363731A0985C249A55A51A710EE0ADF94910A0891";
    public const string MacV1052AssemblyCSharpSha256 =
        "46B723AD8E3DF5ADF7186EC32B220C338E26C1CC79369E01213C091155073BDC";
    public const string MacV1052FirstPassSha256 =
        "CAFE3F4FC522B3AF33A10CB363731A0985C249A55A51A710EE0ADF94910A0891";

    // Retained for source compatibility. These names identify the original Windows baseline only;
    // runtime admission always matches one complete baseline pair below.
    public const string ExpectedAssemblyCSharpSha256 = WindowsAssemblyCSharpSha256;
    public const string ExpectedFirstPassSha256 = WindowsFirstPassSha256;

    public static GameAssemblyBaseline WindowsSteamBaseline => new(
        WindowsBaselineId,
        WindowsAssemblyCSharpSha256,
        WindowsFirstPassSha256);

    public static GameAssemblyBaseline MacSteamBaseline => new(
        MacBaselineId,
        MacAssemblyCSharpSha256,
        MacFirstPassSha256);

    public static GameAssemblyBaseline MacV1052SteamBaseline => new(
        MacV1052BaselineId,
        MacV1052AssemblyCSharpSha256,
        MacV1052FirstPassSha256);

    public static AssemblyAuditResult Check(string gameRoot)
    {
        if (!TryResolveManagedDirectory(gameRoot, out var managedDirectory, out var discoveryFailure))
        {
            if (managedDirectory.Length == 0)
                managedDirectory = DiagnosticManagedDirectory(gameRoot);
            return CreateResult(managedDirectory, discoveryFailure);
        }

        return CreateResult(managedDirectory, string.Empty);
    }

    /// <summary>
    /// Resolves only fixed platform-relative layouts. No user or machine-specific path is retained.
    /// Ambiguous installations fail closed instead of selecting one candidate by enumeration order.
    /// </summary>
    public static bool TryResolveManagedDirectory(
        string gameRoot,
        out string managedDirectory,
        out string reason)
    {
        managedDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            reason = "The game root is empty.";
            return false;
        }

        string fullRoot;
        try { fullRoot = Path.GetFullPath(gameRoot); }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or IOException)
        {
            reason = "The game root is not a valid path.";
            return false;
        }

        var candidates = CandidateManagedDirectories(fullRoot);
        string? complete = null;
        string? partial = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var hasMain = File.Exists(Path.Combine(candidate, "Assembly-CSharp.dll"));
            var hasFirstPass = File.Exists(Path.Combine(candidate, "Assembly-CSharp-firstpass.dll"));
            if (hasMain && hasFirstPass)
            {
                if (complete is not null && !PathsEqual(complete, candidate))
                {
                    reason = "The game root contains more than one complete managed-assembly layout.";
                    return false;
                }
                complete = candidate;
            }
            else if (hasMain || hasFirstPass)
            {
                partial ??= candidate;
            }
        }

        if (complete is not null)
        {
            managedDirectory = complete;
            reason = string.Empty;
            return true;
        }

        if (partial is not null)
        {
            managedDirectory = partial;
            reason = "The managed directory does not contain both required game assemblies.";
            return false;
        }

        reason = "The game root does not contain a supported managed-assembly layout.";
        return false;
    }

    internal static GameAssemblyBaseline? MatchBaseline(
        string? assemblyCSharpSha256,
        string? firstPassSha256)
    {
        var windows = WindowsSteamBaseline;
        if (windows.Matches(assemblyCSharpSha256, firstPassSha256)) return windows;
        var mac = MacSteamBaseline;
        if (mac.Matches(assemblyCSharpSha256, firstPassSha256)) return mac;
        var macV1052 = MacV1052SteamBaseline;
        return macV1052.Matches(assemblyCSharpSha256, firstPassSha256) ? macV1052 : null;
    }

    private static AssemblyAuditResult CreateResult(string managedDirectory, string discoveryFailure)
    {
        var main = CheckOne(Path.Combine(managedDirectory, "Assembly-CSharp.dll"));
        var firstPass = CheckOne(Path.Combine(managedDirectory, "Assembly-CSharp-firstpass.dll"));
        var baseline = discoveryFailure.Length == 0
            ? MatchBaseline(main.ActualSha256, firstPass.ActualSha256)
            : null;
        if (baseline is { } matched)
        {
            main = main.WithExpected(matched.AssemblyCSharpSha256);
            firstPass = firstPass.WithExpected(matched.FirstPassSha256);
        }
        return new AssemblyAuditResult(main, firstPass, baseline, discoveryFailure);
    }

    private static AssemblyHashResult CheckOne(string path)
    {
        if (!File.Exists(path)) return new AssemblyHashResult(path, null);
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        return new AssemblyHashResult(path, hash);
    }

    private static List<string> CandidateManagedDirectories(string fullRoot)
    {
        var candidates = new List<string>(3);
        AddCandidate(candidates, Path.Combine(fullRoot, "Orb Of Creation_Data", "Managed"));
        AddCandidate(candidates, Path.Combine(fullRoot, "Contents", "Resources", "Data", "Managed"));
        AddCandidate(candidates, Path.Combine(
            fullRoot,
            "Orb Of Creation.app",
            "Contents",
            "Resources",
            "Data",
            "Managed"));
        return candidates;
    }

    private static void AddCandidate(ICollection<string> candidates, string candidate)
    {
        foreach (var existing in candidates)
            if (PathsEqual(existing, candidate)) return;
        candidates.Add(candidate);
    }

    private static string DiagnosticManagedDirectory(string gameRoot)
    {
        var root = string.IsNullOrWhiteSpace(gameRoot) ? string.Empty : gameRoot;
        if (root.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(root, "Contents", "Resources", "Data", "Managed");
        return Path.Combine(root, "Orb Of Creation_Data", "Managed");
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}

public readonly struct GameAssemblyBaseline : IEquatable<GameAssemblyBaseline>
{
    public GameAssemblyBaseline(
        string id,
        string assemblyCSharpSha256,
        string firstPassSha256)
    {
        Id = id ?? string.Empty;
        AssemblyCSharpSha256 = assemblyCSharpSha256 ?? string.Empty;
        FirstPassSha256 = firstPassSha256 ?? string.Empty;
    }

    public string Id { get; }
    public string AssemblyCSharpSha256 { get; }
    public string FirstPassSha256 { get; }
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Id) &&
        AssemblyCSharpSha256.Length == 64 &&
        FirstPassSha256.Length == 64;

    public bool Matches(string? assemblyCSharpSha256, string? firstPassSha256) =>
        string.Equals(AssemblyCSharpSha256, assemblyCSharpSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(FirstPassSha256, firstPassSha256, StringComparison.OrdinalIgnoreCase);

    public bool Equals(GameAssemblyBaseline other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal) &&
        string.Equals(AssemblyCSharpSha256, other.AssemblyCSharpSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(FirstPassSha256, other.FirstPassSha256, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is GameAssemblyBaseline other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        Id,
        StringComparer.OrdinalIgnoreCase.GetHashCode(AssemblyCSharpSha256 ?? string.Empty),
        StringComparer.OrdinalIgnoreCase.GetHashCode(FirstPassSha256 ?? string.Empty));
    public static bool operator ==(GameAssemblyBaseline left, GameAssemblyBaseline right) => left.Equals(right);
    public static bool operator !=(GameAssemblyBaseline left, GameAssemblyBaseline right) => !left.Equals(right);
}

public sealed class AssemblyAuditResult
{
    public AssemblyAuditResult(AssemblyHashResult assemblyCSharp, AssemblyHashResult assemblyCSharpFirstPass)
        : this(
            assemblyCSharp,
            assemblyCSharpFirstPass,
            GameAssemblyAudit.MatchBaseline(
                assemblyCSharp?.ActualSha256,
                assemblyCSharpFirstPass?.ActualSha256),
            string.Empty)
    {
    }

    internal AssemblyAuditResult(
        AssemblyHashResult assemblyCSharp,
        AssemblyHashResult assemblyCSharpFirstPass,
        GameAssemblyBaseline? matchedBaseline,
        string discoveryFailure)
    {
        AssemblyCSharp = assemblyCSharp ?? throw new ArgumentNullException(nameof(assemblyCSharp));
        AssemblyCSharpFirstPass = assemblyCSharpFirstPass ??
            throw new ArgumentNullException(nameof(assemblyCSharpFirstPass));
        MatchedBaseline = matchedBaseline;
        DiscoveryFailure = discoveryFailure ?? string.Empty;
    }

    public AssemblyHashResult AssemblyCSharp { get; }
    public AssemblyHashResult AssemblyCSharpFirstPass { get; }
    public GameAssemblyBaseline? MatchedBaseline { get; }
    public string MatchedBaselineId => MatchedBaseline?.Id ?? string.Empty;
    public string DiscoveryFailure { get; }
    public bool MatchesExpected => DiscoveryFailure.Length == 0 && MatchedBaseline is { IsValid: true };
}

public sealed class AssemblyHashResult
{
    public AssemblyHashResult(string path, string? actualSha256)
        : this(path, string.Empty, actualSha256)
    {
    }

    // Retained for source compatibility. Whole-installation admission is decided only by
    // AssemblyAuditResult.MatchesExpected after both actual hashes form one known pair.
    public AssemblyHashResult(string path, string expectedSha256, string? actualSha256)
    {
        Path = path ?? string.Empty;
        ExpectedSha256 = expectedSha256 ?? string.Empty;
        ActualSha256 = actualSha256;
    }

    public string Path { get; }
    public string ExpectedSha256 { get; }
    public string? ActualSha256 { get; }
    public bool Exists => ActualSha256 is not null;
    public bool MatchesExpected =>
        ExpectedSha256.Length != 0 &&
        ActualSha256 is not null &&
        string.Equals(ActualSha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    internal AssemblyHashResult WithExpected(string expectedSha256) =>
        new(Path, expectedSha256, ActualSha256);
}
