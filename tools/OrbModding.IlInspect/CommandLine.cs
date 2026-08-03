using System;
using System.Collections.Generic;
using System.IO;

namespace OrbModding.IlInspect;

internal sealed record InspectionCommand(string AssemblyPath, string Verb, string Query);

internal sealed class CommandLineException : Exception
{
    internal CommandLineException(string message) : base(message)
    {
    }
}

internal static class CommandLine
{
    internal const string Usage =
        "Usage: OrbModding.IlInspect [--game-dir <path>] [--assembly <name.dll>] " +
        "<type|method|callers|implementers|strings> <query>";

    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "type",
        "method",
        "callers",
        "implementers",
        "strings",
    };

    internal static InspectionCommand Parse(
        IReadOnlyList<string> args,
        Func<string?> gameDirectoryEnvironment)
    {
        string? gameDirectory = null;
        var assemblyName = "Assembly-CSharp.dll";
        var positionals = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--game-dir":
                    gameDirectory = ReadOption(args, ref index, "--game-dir");
                    break;
                case "--assembly":
                    assemblyName = ReadOption(args, ref index, "--assembly");
                    break;
                default:
                    if (args[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new CommandLineException($"Unknown option: {args[index]}");
                    }
                    positionals.Add(args[index]);
                    break;
            }
        }

        if (positionals.Count != 2 || !Verbs.Contains(positionals[0]))
        {
            throw new CommandLineException("Expected one query verb and one query value.");
        }

        gameDirectory ??= gameDirectoryEnvironment();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new CommandLineException(
                "No game directory was provided. Pass --game-dir or set OOC_GAME_DIR.");
        }

        ValidateAssemblyName(assemblyName);
        var managedDirectory = ResolveManagedDirectory(gameDirectory);
        var assemblyPath = Path.GetFullPath(Path.Combine(managedDirectory, assemblyName));
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Target assembly was not found: {assemblyPath}", assemblyPath);
        }

        return new InspectionCommand(assemblyPath, positionals[0], positionals[1]);
    }

    private static string ReadOption(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CommandLineException($"{option} requires a value.");
        }
        return args[index];
    }

    private static void ValidateAssemblyName(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            !assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(assemblyName) ||
            !string.Equals(Path.GetFileName(assemblyName), assemblyName, StringComparison.Ordinal))
        {
            throw new CommandLineException(
                "--assembly must be the name of one DLL directly under the Managed directory.");
        }
    }

    private static string ResolveManagedDirectory(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        var candidates = new[]
        {
            root,
            Path.Combine(root, "Orb Of Creation_Data", "Managed"),
            Path.Combine(root, "Contents", "Resources", "Data", "Managed"),
            Path.Combine(root, "Orb Of Creation.app", "Contents", "Resources", "Data", "Managed"),
        };

        foreach (var candidate in candidates)
        {
            if (string.Equals(Path.GetFileName(candidate), "Managed", StringComparison.Ordinal) &&
                Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            $"No Orb of Creation Managed directory was found under: {root}");
    }
}
