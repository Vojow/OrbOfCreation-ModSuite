using System.Globalization;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// Lists the generation-keyed publication stores a session recorded. The files are text, so the report
/// says which generations exist and leaves reading one to whoever wants it.
/// </summary>
internal static class ManualFullTraceStoreView
{
    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        var entries = session.PublicationStores();
        writer.WriteLine("## Publication stores");
        writer.WriteLine();
        if (entries.Count == 0)
        {
            writer.WriteLine("No publication stores were recorded with this session.");
            writer.WriteLine();
            return;
        }

        writer.WriteLine("| Store | Generation | Values | File |");
        writer.WriteLine("|---|---:|---:|---|");
        foreach (var entry in entries)
        {
            writer.WriteLine(
                $"| {entry.Store} | {entry.Generation.ToString("N0", CultureInfo.InvariantCulture)} | " +
                $"{entry.ValueCount.ToString("N0", CultureInfo.InvariantCulture)} | `{entry.FileName}` |");
        }
        writer.WriteLine();
    }
}
