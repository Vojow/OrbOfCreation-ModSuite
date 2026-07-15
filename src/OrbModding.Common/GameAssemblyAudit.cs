using System;
using System.IO;
using System.Security.Cryptography;

namespace OrbModding.Common;

public static class GameAssemblyAudit
{
    public const string ExpectedAssemblyCSharpSha256 =
        "5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F";

    public const string ExpectedFirstPassSha256 =
        "D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A";

    public static AssemblyAuditResult Check(string gameRoot)
    {
        var managedDir = Path.Combine(gameRoot, "Orb Of Creation_Data", "Managed");
        var mainAssembly = Path.Combine(managedDir, "Assembly-CSharp.dll");
        var firstPassAssembly = Path.Combine(managedDir, "Assembly-CSharp-firstpass.dll");

        return new AssemblyAuditResult(
            CheckOne(mainAssembly, ExpectedAssemblyCSharpSha256),
            CheckOne(firstPassAssembly, ExpectedFirstPassSha256));
    }

    private static AssemblyHashResult CheckOne(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            return new AssemblyHashResult(path, expectedSha256, null);
        }

        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        return new AssemblyHashResult(path, expectedSha256, hash);
    }
}

public sealed class AssemblyAuditResult
{
    public AssemblyAuditResult(AssemblyHashResult assemblyCSharp, AssemblyHashResult assemblyCSharpFirstPass)
    {
        AssemblyCSharp = assemblyCSharp;
        AssemblyCSharpFirstPass = assemblyCSharpFirstPass;
    }

    public AssemblyHashResult AssemblyCSharp { get; }

    public AssemblyHashResult AssemblyCSharpFirstPass { get; }

    public bool MatchesExpected => AssemblyCSharp.MatchesExpected && AssemblyCSharpFirstPass.MatchesExpected;
}

public sealed class AssemblyHashResult
{
    public AssemblyHashResult(string path, string expectedSha256, string? actualSha256)
    {
        Path = path;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
    }

    public string Path { get; }

    public string ExpectedSha256 { get; }

    public string? ActualSha256 { get; }

    public bool Exists => ActualSha256 is not null;

    public bool MatchesExpected =>
        ActualSha256 is not null &&
        string.Equals(ActualSha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
}
