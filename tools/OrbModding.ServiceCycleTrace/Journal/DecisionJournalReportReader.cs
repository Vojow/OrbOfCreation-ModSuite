using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Journal;

internal static class DecisionJournalReportReader
{
    internal static DecisionJournalReportData Read(string path)
    {
        var directory = new DecisionJournalDirectory(path);
        var inventory = directory.Inventory();
        var assembler = new DecisionJournalWindowAssembler();
        var analysis = new DecisionJournalAnalysis();
        var spool = new TemporaryTextSpool();
        try
        {
            var lineage = new DecisionJournalLineageWriter(spool.Writer);
            if (inventory.HasSegments)
            {
                for (var value = (long)inventory.FirstOrdinal; value <= inventory.LastOrdinal; value++)
                {
                    var segment = directory.ReadSegment(checked((int)value), out var encodedBytes);
                    assembler.Add(segment, encodedBytes);
                    lineage.Write(segment);
                    for (var index = 0; index < segment.Records.Length; index++)
                        analysis.Observe(segment.Run, in segment.Records[index]);
                }
            }
            var window = assembler.Complete();
            if (window.SegmentCount != checked((ulong)inventory.Count))
                throw new InvalidDataException("The decision-journal inventory changed while it was read.");
            lineage.Complete(window.RecordCount != 0);
            spool.Seal();
            return new DecisionJournalReportData(
                directory,
                window,
                analysis.Complete(),
                spool);
        }
        catch
        {
            spool.Dispose();
            throw;
        }
    }
}
