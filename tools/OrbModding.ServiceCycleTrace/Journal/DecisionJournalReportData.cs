using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.ServiceCycleTrace.IO;

namespace OrbModding.ServiceCycleTrace.Journal;

internal sealed class DecisionJournalReportData : IDisposable
{
    private readonly DecisionJournalDirectory _directory;
    private readonly TemporaryTextSpool _lineage;

    internal DecisionJournalReportData(
        DecisionJournalDirectory directory,
        DecisionJournalWindowDocument window,
        DecisionJournalAnalysisDocument analysis,
        TemporaryTextSpool lineage)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        _lineage = lineage ?? throw new ArgumentNullException(nameof(lineage));
    }

    internal DecisionJournalWindowDocument Window { get; }
    internal DecisionJournalAnalysisDocument Analysis { get; }
    internal void EnsureSafeReportPath(string path) => _directory.EnsureSafeReportPath(path);
    internal void WriteLineage(TextWriter writer) => _lineage.CopyTo(writer);
    public void Dispose() => _lineage.Dispose();
}
