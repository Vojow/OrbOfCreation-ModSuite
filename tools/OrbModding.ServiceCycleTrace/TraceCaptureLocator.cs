using OrbModding.ServiceCycleTrace.ManualTrace;

namespace OrbModding.ServiceCycleTrace;

/// <summary>The full-trace session the tool will read, and the run folder it was found in.</summary>
internal sealed class TraceCaptureSelection
{
    internal TraceCaptureSelection(
        string fullSessionDirectory,
        string? runDirectory,
        IReadOnlyList<string> notes)
    {
        FullSessionDirectory = fullSessionDirectory;
        RunDirectory = runDirectory;
        Notes = notes;
    }

    internal string FullSessionDirectory { get; }

    /// <summary>
    /// The folder the correlated siblings live in, absent when the caller named a session directory
    /// that sits outside a run folder.
    /// </summary>
    internal string? RunDirectory { get; }

    /// <summary>What the resolution decided, for a caller that should say so out loud.</summary>
    internal IReadOnlyList<string> Notes { get; }
}

/// <summary>
/// Turns whatever directory a caller named into the one session the tool reads.
/// </summary>
/// <remarks>
/// The runtime writes <c>trace/run-&lt;timestamp&gt;/full/session-&lt;id&gt;/</c>, and every level of
/// that path is something a person reasonably points a trace tool at. Requiring the innermost one
/// made the ordinary case — "read the trace I just recorded" — a directory-listing exercise, so all
/// three are accepted and resolved here rather than in each mode.
/// </remarks>
internal static class TraceCaptureLocator
{
    private const string FullChild = "full";
    private const string RunPrefix = "run-";
    private const string SessionPattern = "session-*";

    internal static TraceCaptureSelection Locate(string inputPath)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputPath));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("The trace input directory does not exist: " + path);

        var name = Path.GetFileName(path);
        if (ManualFullTraceSessionDirectory.IsSessionDirectoryName(name))
            return new TraceCaptureSelection(path, RunDirectoryOfSession(path), Array.Empty<string>());

        if (string.Equals(name, FullChild, StringComparison.Ordinal) && HasSession(path))
            return new TraceCaptureSelection(
                SingleSession(path),
                Path.GetDirectoryName(path),
                Array.Empty<string>());

        var full = Path.Combine(path, FullChild);
        if (HasSession(full))
            return new TraceCaptureSelection(SingleSession(full), path, Array.Empty<string>());

        var runs = RunFolders(path);
        if (runs.Count != 0)
        {
            // The folder name carries a fixed-width UTC timestamp, so ordinal order is chronological
            // order without trusting filesystem timestamps a copy would not have preserved.
            runs.Sort(StringComparer.Ordinal);
            var chosen = runs[^1];
            return new TraceCaptureSelection(
                SingleSession(Path.Combine(path, chosen, FullChild)),
                Path.Combine(path, chosen),
                SelectionNotes(runs, chosen));
        }

        throw new InvalidDataException(
            "The trace input holds no full-trace session: " + path + ". Point the tool at a session " +
            "directory (full/session-<id>), at the run folder that holds it (run-<timestamp>/), or at " +
            "the trace root that holds the run folders.");
    }

    private static IReadOnlyList<string> SelectionNotes(List<string> runs, string chosen)
    {
        if (runs.Count == 1)
            return new[] { "Read the trace root's only run folder " + chosen + "." };
        var others = string.Join(", ", runs.GetRange(0, runs.Count - 1));
        return new[]
        {
            "Read the newest of " + runs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " run folders: " + chosen + ".",
            "Not read: " + others + ". Name one directly to read it instead.",
        };
    }

    private static string? RunDirectoryOfSession(string sessionPath)
    {
        var parent = Path.GetDirectoryName(sessionPath);
        if (parent is null ||
            !string.Equals(Path.GetFileName(parent), FullChild, StringComparison.Ordinal))
            return null;
        return Path.GetDirectoryName(parent);
    }

    private static List<string> RunFolders(string root)
    {
        var folders = new List<string>();
        foreach (var path in Directory.EnumerateDirectories(root, RunPrefix + "*"))
            if (HasSession(Path.Combine(path, FullChild))) folders.Add(Path.GetFileName(path));
        return folders;
    }

    private static bool HasSession(string fullDirectory) =>
        Directory.Exists(fullDirectory) && Sessions(fullDirectory).Length != 0;

    private static string SingleSession(string fullDirectory)
    {
        var sessions = Sessions(fullDirectory);
        if (sessions.Length == 0)
            throw new InvalidDataException(
                "The full-trace directory holds no session: " + fullDirectory + ".");
        if (sessions.Length != 1)
        {
            var names = new string[sessions.Length];
            for (var index = 0; index < sessions.Length; index++)
                names[index] = Path.GetFileName(sessions[index]);
            Array.Sort(names, StringComparer.Ordinal);
            throw new InvalidDataException(
                "The full-trace directory holds " +
                sessions.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " sessions, and a report reads exactly one: " + string.Join(", ", names) +
                ". Name the session directory directly.");
        }
        return sessions[0];
    }

    private static string[] Sessions(string fullDirectory)
    {
        if (!Directory.Exists(fullDirectory)) return Array.Empty<string>();
        var candidates = Directory.GetDirectories(fullDirectory, SessionPattern, SearchOption.TopDirectoryOnly);
        var kept = new List<string>(candidates.Length);
        foreach (var candidate in candidates)
            if (ManualFullTraceSessionDirectory.IsSessionDirectoryName(Path.GetFileName(candidate)))
                kept.Add(candidate);
        return kept.ToArray();
    }
}
