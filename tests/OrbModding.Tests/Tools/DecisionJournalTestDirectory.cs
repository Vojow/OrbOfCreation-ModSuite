using System;
using System.Globalization;
using System.IO;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

namespace OrbModding.Tests.Tools;

internal sealed class DecisionJournalTestDirectory : IDisposable
{
    internal DecisionJournalTestDirectory()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "orb-decision-journal-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string WriteSegment(
        int ordinal,
        ulong run,
        ulong firstSequence,
        params DecisionJournalRecord[] records)
    {
        var bytes = new byte[DecisionJournalSegmentCodec.GetEncodedLength(records.Length)];
        DecisionJournalSegmentCodec.Encode(
            new DecisionJournalRunId(run),
            checked((ulong)ordinal),
            firstSequence,
            records,
            bytes);
        var path = SegmentPath(ordinal);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal string SegmentPath(int ordinal) => Path.Combine(
        Root,
        "journal-" + ordinal.ToString("D6", CultureInfo.InvariantCulture) + ".osjd");

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
