using OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Format;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

internal sealed class ManualFullTraceSession
{
    private readonly ManualFullTraceSessionDirectory _directory;

    internal ManualFullTraceSession(
        ManualFullTraceSessionDirectory directory,
        in FullTraceSessionDocument document)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        Document = document;
    }

    internal string Name => _directory.Name;
    internal FullTraceSessionDocument Document { get; }

    internal void EnsureSafeReportPath(string path) => _directory.EnsureSafeReportPath(path);

    internal IEnumerable<FullTraceSegmentDocument> Segments()
    {
        for (var ordinal = 0UL; ordinal < Document.SegmentCount; ordinal++)
            yield return _directory.ReadSegment(ordinal);
    }
}
