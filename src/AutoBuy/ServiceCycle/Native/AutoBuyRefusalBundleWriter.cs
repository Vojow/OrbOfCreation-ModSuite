using System;
using System.Collections.Generic;
using System.IO;

namespace OrbAutomata;

/// <summary>
/// Where a refusal bundle is put. Separated from the responder so recoverable and stand-down policy
/// can be tested without a filesystem, and so a failure to write one is a value the responder handles
/// rather than an exception thrown into the action path.
/// </summary>
internal interface IAutoBuyRefusalBundlePort
{
    /// <summary>
    /// Writes one bundle and answers where it went. False means it could not be written; the caller
    /// still applies its classified policy, and still says so, without a path.
    /// </summary>
    bool TryWrite(string contents, DateTime utcNow, out string path);
}

/// <summary>
/// Writes refusal bundles to a stable directory under the suite's trace root and keeps the newest
/// few.
/// </summary>
/// <remarks>
/// <para>
/// A stable directory rather than a per-launch one, because a bundle is written at the moment Auto
/// Buy records a refusal and is read some time later, by someone who was told a path — the folder
/// they were told about has to be the folder it is still in after a restart.
/// </para>
/// <para>
/// Nothing here throws. The bundle exists so a person can find out why the suite stopped buying; a
/// writer that could fault the action path would turn a diagnosis into a second fault.
/// </para>
/// </remarks>
internal sealed class AutoBuyRefusalBundleWriter : IAutoBuyRefusalBundlePort
{
    /// <summary>Bundles that survive a write, counting the one being written.</summary>
    internal const int RetainedBundles = 8;

    private readonly Func<string> _directory;

    public AutoBuyRefusalBundleWriter(Func<string> directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public bool TryWrite(string contents, DateTime utcNow, out string path)
    {
        path = string.Empty;
        try
        {
            var directory = _directory();
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, AutoBuyRefusalBundle.FileName(utcNow));
            File.WriteAllText(target, contents ?? string.Empty);
            path = target;
            Sweep(directory, Path.GetFileName(target));
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes the oldest bundles until at most <see cref="RetainedBundles"/> remain. The file name
    /// carries a fixed-width UTC timestamp, so ordinal order is chronological order without trusting
    /// filesystem timestamps a copied directory would not preserve.
    /// </summary>
    private static void Sweep(string directory, string current)
    {
        var names = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, AutoBuyRefusalBundle.FileNamePrefix + "*.txt"))
            {
                var name = Path.GetFileName(file);
                if (!string.Equals(name, current, StringComparison.Ordinal)) names.Add(name);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return;
        }

        names.Sort(StringComparer.Ordinal);
        var removable = names.Count - (RetainedBundles - 1);
        for (var index = 0; index < removable; index++)
        {
            try
            {
                File.Delete(Path.Combine(directory, names[index]));
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                // A file someone is reading, or one the filesystem refuses, waits for the next write.
            }
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException;
}
