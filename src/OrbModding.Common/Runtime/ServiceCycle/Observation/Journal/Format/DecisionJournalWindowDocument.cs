using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal sealed class DecisionJournalWindowDocument
{
    private readonly DecisionJournalRunDocument[] _runs;

    internal DecisionJournalWindowDocument(
        ulong firstStorageOrdinal,
        ulong lastStorageOrdinal,
        ulong segmentCount,
        ulong recordCount,
        ulong segmentBytes,
        DecisionJournalRunDocument[] runs)
    {
        FirstStorageOrdinal = firstStorageOrdinal;
        LastStorageOrdinal = lastStorageOrdinal;
        SegmentCount = segmentCount;
        RecordCount = recordCount;
        SegmentBytes = segmentBytes;
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    }

    internal bool HasSegments => SegmentCount != 0;
    internal ulong FirstStorageOrdinal { get; }
    internal ulong LastStorageOrdinal { get; }
    internal ulong SegmentCount { get; }
    internal ulong RecordCount { get; }
    internal ulong SegmentBytes { get; }
    internal int RunCount => _runs.Length;
    internal DecisionJournalRunDocument GetRun(int index) => _runs[index];
}

internal readonly struct DecisionJournalRunDocument
{
    internal DecisionJournalRunDocument(
        DecisionJournalRunId run,
        ulong firstStorageOrdinal,
        ulong lastStorageOrdinal,
        ulong firstRecordSequence,
        ulong lastRecordSequence,
        ulong segmentCount,
        ulong recordCount,
        long firstTimestampTicks,
        long lastTimestampTicks)
    {
        Run = run;
        FirstStorageOrdinal = firstStorageOrdinal;
        LastStorageOrdinal = lastStorageOrdinal;
        FirstRecordSequence = firstRecordSequence;
        LastRecordSequence = lastRecordSequence;
        SegmentCount = segmentCount;
        RecordCount = recordCount;
        FirstTimestampTicks = firstTimestampTicks;
        LastTimestampTicks = lastTimestampTicks;
    }

    internal DecisionJournalRunId Run { get; }
    internal ulong FirstStorageOrdinal { get; }
    internal ulong LastStorageOrdinal { get; }
    internal ulong FirstRecordSequence { get; }
    internal ulong LastRecordSequence { get; }
    internal ulong SegmentCount { get; }
    internal ulong RecordCount { get; }
    internal long FirstTimestampTicks { get; }
    internal long LastTimestampTicks { get; }
}
