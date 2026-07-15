using System;
using System.IO;
using Xunit;

namespace OrbModding.GameContractTests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class GameAssemblyFactAttribute : FactAttribute
{
    public GameAssemblyFactAttribute()
    {
        if (!GameAssemblyPaths.TryResolve(out _, out var reason))
        {
            Skip = reason;
        }
    }
}

internal sealed class GameAssemblyPaths
{
    private GameAssemblyPaths(string gameRoot, string assemblyCSharp, string firstPass)
    {
        GameRoot = gameRoot;
        AssemblyCSharp = assemblyCSharp;
        FirstPass = firstPass;
    }

    public string GameRoot { get; }

    public string AssemblyCSharp { get; }

    public string FirstPass { get; }

    public static bool TryResolve(out GameAssemblyPaths paths, out string reason)
    {
        var gameRoot = Environment.GetEnvironmentVariable("OOC_GAME_DIR");
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            paths = null!;
            reason = "Set OOC_GAME_DIR to run installed-game contract tests.";
            return false;
        }

        var managed = Path.Combine(gameRoot, "Orb Of Creation_Data", "Managed");
        var assemblyCSharp = Path.Combine(managed, "Assembly-CSharp.dll");
        var firstPass = Path.Combine(managed, "Assembly-CSharp-firstpass.dll");
        if (!File.Exists(assemblyCSharp) || !File.Exists(firstPass))
        {
            paths = null!;
            reason = "OOC_GAME_DIR does not contain both audited game assemblies.";
            return false;
        }

        paths = new GameAssemblyPaths(Path.GetFullPath(gameRoot), assemblyCSharp, firstPass);
        reason = string.Empty;
        return true;
    }

    public static GameAssemblyPaths Require()
    {
        if (!TryResolve(out var paths, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        return paths;
    }
}
