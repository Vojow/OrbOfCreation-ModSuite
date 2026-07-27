using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

internal readonly struct DecisionJournalRunId : IEquatable<DecisionJournalRunId>
{
    internal DecisionJournalRunId(ulong value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    internal ulong Value { get; }
    internal bool IsValid => Value != 0;
    public bool Equals(DecisionJournalRunId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is DecisionJournalRunId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(DecisionJournalRunId left, DecisionJournalRunId right) => left.Equals(right);
    public static bool operator !=(DecisionJournalRunId left, DecisionJournalRunId right) => !left.Equals(right);
}

internal sealed class DecisionJournalSegmentDocument
{
    internal DecisionJournalSegmentDocument(
        DecisionJournalRunId run,
        ulong ordinal,
        ulong firstRecordSequence,
        DecisionJournalRecord[] records)
    {
        Run = run;
        Ordinal = ordinal;
        FirstRecordSequence = firstRecordSequence;
        Records = records ?? throw new ArgumentNullException(nameof(records));
    }

    internal DecisionJournalRunId Run { get; }
    internal ulong Ordinal { get; }
    internal ulong FirstRecordSequence { get; }
    internal DecisionJournalRecord[] Records { get; }
}
