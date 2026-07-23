using System;
using System.Collections.Generic;
using System.IO;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal sealed class DecisionJournalWindowAssembler
{
    private readonly HashSet<DecisionJournalRunId> _seenRuns = new();
    private readonly List<DecisionJournalRunDocument> _runs = new();
    private bool _complete;
    private bool _failed;
    private bool _hasSegment;
    private DecisionJournalRunId _run;
    private ulong _firstStorageOrdinal;
    private ulong _lastStorageOrdinal;
    private ulong _firstRecordSequence;
    private ulong _lastRecordSequence;
    private ulong _runSegments;
    private ulong _runRecords;
    private long _firstTimestampTicks;
    private long _lastTimestampTicks;
    private ulong _segments;
    private ulong _records;
    private ulong _bytes;

    internal void Add(DecisionJournalSegmentDocument segment, int encodedBytes)
    {
        if (_complete || _failed)
            throw new InvalidOperationException("The decision-journal window cannot accept another segment.");
        try
        {
            if (segment is null) throw new ArgumentNullException(nameof(segment));
            AddCore(segment, encodedBytes);
        }
        catch (OverflowException exception)
        {
            _failed = true;
            throw new InvalidDataException("The decision-journal window exceeds its numeric bounds.", exception);
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    internal DecisionJournalWindowDocument Complete()
    {
        if (_complete || _failed)
            throw new InvalidOperationException("The decision-journal window cannot be completed.");
        _complete = true;
        if (_hasSegment) CompleteRun();
        return new DecisionJournalWindowDocument(
            _hasSegment ? _runs[0].FirstStorageOrdinal : 0,
            _hasSegment ? _lastStorageOrdinal : 0,
            _segments,
            _records,
            _bytes,
            _runs.ToArray());
    }

    private void AddCore(DecisionJournalSegmentDocument segment, int encodedBytes)
    {
        var expectedBytes = DecisionJournalSegmentCodec.GetEncodedLength(segment.Records.Length);
        if (encodedBytes != expectedBytes)
            throw new InvalidDataException("The decision-journal segment length is inconsistent.");

        if (_hasSegment && segment.Ordinal != checked(_lastStorageOrdinal + 1))
            throw new InvalidDataException("The retained decision-journal storage ordinals are not contiguous.");

        var lastRecordSequence = checked(
            segment.FirstRecordSequence + (ulong)segment.Records.Length - 1);
        SegmentTimestampRange(segment.Records, out var firstTimestamp, out var lastTimestamp);

        if (!_hasSegment)
        {
            _firstStorageOrdinal = segment.Ordinal;
            BeginRun(segment, lastRecordSequence, firstTimestamp, lastTimestamp);
            _hasSegment = true;
        }
        else if (segment.Run == _run)
        {
            if (segment.FirstRecordSequence != checked(_lastRecordSequence + 1))
                throw new InvalidDataException("Decision-journal record sequences are not contiguous within a run.");
            ExtendRun(segment, lastRecordSequence, firstTimestamp, lastTimestamp);
        }
        else
        {
            if (segment.FirstRecordSequence != 1)
                throw new InvalidDataException("A later decision-journal run must begin at record sequence one.");
            if (_seenRuns.Contains(segment.Run))
                throw new InvalidDataException("A decision-journal run identity reappears after another run.");
            CompleteRun();
            BeginRun(segment, lastRecordSequence, firstTimestamp, lastTimestamp);
        }

        _lastStorageOrdinal = segment.Ordinal;
        _segments = checked(_segments + 1);
        _records = checked(_records + (ulong)segment.Records.Length);
        _bytes = checked(_bytes + (ulong)encodedBytes);
    }

    private void BeginRun(
        DecisionJournalSegmentDocument segment,
        ulong lastRecordSequence,
        long firstTimestamp,
        long lastTimestamp)
    {
        if (!_seenRuns.Add(segment.Run))
            throw new InvalidDataException("A decision-journal run identity reappears after another run.");
        _run = segment.Run;
        _firstStorageOrdinal = segment.Ordinal;
        _lastStorageOrdinal = segment.Ordinal;
        _firstRecordSequence = segment.FirstRecordSequence;
        _lastRecordSequence = lastRecordSequence;
        _runSegments = 1;
        _runRecords = checked((ulong)segment.Records.Length);
        _firstTimestampTicks = firstTimestamp;
        _lastTimestampTicks = lastTimestamp;
    }

    private void ExtendRun(
        DecisionJournalSegmentDocument segment,
        ulong lastRecordSequence,
        long firstTimestamp,
        long lastTimestamp)
    {
        _lastStorageOrdinal = segment.Ordinal;
        _lastRecordSequence = lastRecordSequence;
        _runSegments = checked(_runSegments + 1);
        _runRecords = checked(_runRecords + (ulong)segment.Records.Length);
        _firstTimestampTicks = Math.Min(_firstTimestampTicks, firstTimestamp);
        _lastTimestampTicks = Math.Max(_lastTimestampTicks, lastTimestamp);
    }

    private void CompleteRun() => _runs.Add(new DecisionJournalRunDocument(
        _run,
        _firstStorageOrdinal,
        _lastStorageOrdinal,
        _firstRecordSequence,
        _lastRecordSequence,
        _runSegments,
        _runRecords,
        _firstTimestampTicks,
        _lastTimestampTicks));

    private static void SegmentTimestampRange(
        DecisionJournalRecord[] records,
        out long firstTimestamp,
        out long lastTimestamp)
    {
        firstTimestamp = long.MaxValue;
        lastTimestamp = 0;
        for (var index = 0; index < records.Length; index++)
        {
            firstTimestamp = Math.Min(firstTimestamp, records[index].FirstTimestampTicks);
            lastTimestamp = Math.Max(lastTimestamp, records[index].LastTimestampTicks);
        }
    }
}
