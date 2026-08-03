using System;
using System.Collections.Generic;
using System.IO;

namespace OrbAutomata;

internal readonly struct AutomataLegacyObservabilityCleanupResult
{
    public AutomataLegacyObservabilityCleanupResult(
        int removedFiles,
        long removedBytes,
        int removedDirectories,
        int unknownEntries,
        int failures)
    {
        RemovedFiles = removedFiles;
        RemovedBytes = removedBytes;
        RemovedDirectories = removedDirectories;
        UnknownEntries = unknownEntries;
        Failures = failures;
    }

    public int RemovedFiles { get; }
    public long RemovedBytes { get; }
    public int RemovedDirectories { get; }
    public int UnknownEntries { get; }
    public int Failures { get; }
    public bool ShouldLog =>
        RemovedFiles != 0 || RemovedDirectories != 0 || UnknownEntries != 0 || Failures != 0;
    public bool HasWarnings => UnknownEntries != 0 || Failures != 0;

    public string Describe() =>
        $"Legacy observability cleanup removed {RemovedFiles} owned files " +
        $"({RemovedBytes} bytes) and {RemovedDirectories} directories from retired " +
        $"trace/full, trace/profile, and replay/auto-harvest storage; left " +
        $"{UnknownEntries} unrecognized entries and encountered {Failures} storage failures.";
}

/// <summary>
/// Removes only retired suite-owned observability files whose exact directory, extension, and
/// four-byte format magic agree. Unknown entries stay in place and make the one startup summary
/// loud rather than widening deletion ownership.
/// </summary>
internal static class AutomataLegacyObservabilityCleanup
{
    private static readonly LegacyTarget[] Targets =
    {
        new("trace/full", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".oscs"] = "OSCS",
            [".oscm"] = "OSCM",
        }),
        new("trace/profile", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".osps"] = "OSPS",
            [".ospm"] = "OSPM",
        }),
        new("replay/auto-harvest", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".oscr"] = "OSCR",
        }),
    };

    internal static AutomataLegacyObservabilityCleanupResult Run(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot))
            throw new ArgumentException("A configuration root is required.", nameof(configRoot));

        var state = new CleanupState();
        var suiteRoot = Path.Combine(configRoot, "OrbOfCreation-ModSuite");
        foreach (var target in Targets)
        {
            var relative = target.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            VisitDirectory(Path.Combine(suiteRoot, relative), target, state);
        }
        return state.ToResult();
    }

    private static void VisitDirectory(string directory, LegacyTarget target, CleanupState state)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        state.UnknownEntries++;
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        VisitDirectory(entry, target, state);
                        continue;
                    }
                    if (!target.TryOwn(entry, out var bytes))
                    {
                        state.UnknownEntries++;
                        continue;
                    }
                    File.Delete(entry);
                    state.RemovedFiles++;
                    state.RemovedBytes = checked(state.RemovedBytes + bytes);
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    state.Failures++;
                }
            }

            using var remaining = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            if (!remaining.MoveNext())
            {
                Directory.Delete(directory);
                state.RemovedDirectories++;
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            state.Failures++;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or OverflowException or System.Security.SecurityException;

    private sealed class LegacyTarget
    {
        private readonly IReadOnlyDictionary<string, string> _magicByExtension;

        internal LegacyTarget(
            string relativePath,
            IReadOnlyDictionary<string, string> magicByExtension)
        {
            RelativePath = relativePath;
            _magicByExtension = magicByExtension;
        }

        internal string RelativePath { get; }

        internal bool TryOwn(string path, out long bytes)
        {
            bytes = 0;
            if (!_magicByExtension.TryGetValue(Path.GetExtension(path), out var expectedMagic))
                return false;

            var info = new FileInfo(path);
            if (info.Length < 4) return false;
            var header = new byte[4];
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Read(header, 0, header.Length) != header.Length) return false;
            }
            for (var index = 0; index < header.Length; index++)
                if (header[index] != expectedMagic[index]) return false;
            bytes = info.Length;
            return true;
        }
    }

    private sealed class CleanupState
    {
        internal int RemovedFiles;
        internal long RemovedBytes;
        internal int RemovedDirectories;
        internal int UnknownEntries;
        internal int Failures;

        internal AutomataLegacyObservabilityCleanupResult ToResult() =>
            new(RemovedFiles, RemovedBytes, RemovedDirectories, UnknownEntries, Failures);
    }
}
