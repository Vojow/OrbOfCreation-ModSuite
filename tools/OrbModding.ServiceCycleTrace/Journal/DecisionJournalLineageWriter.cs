using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

namespace OrbModding.ServiceCycleTrace.Journal;

internal sealed class DecisionJournalLineageWriter
{
    private readonly TextWriter _writer;
    private DecisionJournalRunId _run;

    internal DecisionJournalLineageWriter(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _writer.WriteLine("## Retained record lineage");
        _writer.WriteLine();
        _writer.WriteLine(
            "Rows follow durable record sequence within each run. They are not a global wall-clock or physical-thread timeline.");
        _writer.WriteLine();
    }

    internal void Write(DecisionJournalSegmentDocument segment)
    {
        if (segment.Run != _run)
        {
            _run = segment.Run;
            _writer.Write("### Run `");
            _writer.Write(segment.Run.Value.ToString("x16", CultureInfo.InvariantCulture));
            _writer.WriteLine("`");
            _writer.WriteLine();
        }

        for (var index = 0; index < segment.Records.Length; index++)
        {
            var sequence = checked(segment.FirstRecordSequence + (ulong)index);
            WriteRecord(sequence, in segment.Records[index]);
        }
    }

    internal void Complete(bool hasRecords)
    {
        if (!hasRecords) _writer.WriteLine("No committed journal records were retained.");
    }

    private void WriteRecord(ulong sequence, in DecisionJournalRecord record)
    {
        _writer.Write("- `#");
        _writer.Write(sequence.ToString(CultureInfo.InvariantCulture));
        _writer.Write("` ticks `");
        _writer.Write(record.FirstTimestampTicks.ToString(CultureInfo.InvariantCulture));
        if (record.LastTimestampTicks != record.FirstTimestampTicks)
        {
            _writer.Write("..");
            _writer.Write(record.LastTimestampTicks.ToString(CultureInfo.InvariantCulture));
        }
        _writer.Write("` (");
        _writer.Write(TraceMetric.ToMilliseconds(
            record.LastTimestampTicks - record.FirstTimestampTicks)
            .ToString("F3", CultureInfo.InvariantCulture));
        _writer.Write(" ms) — ");

        if (record.Kind == DecisionJournalRecordKind.DecisionSpan)
            WriteDecision(in record);
        else
            WriteTransition(in record);
        _writer.WriteLine();
    }

    private void WriteDecision(in DecisionJournalRecord record)
    {
        _writer.Write("service `");
        _writer.Write(record.Service.Value.ToString(CultureInfo.InvariantCulture));
        _writer.Write("` decision span x");
        _writer.Write(record.RepeatCount.ToString("N0", CultureInfo.InvariantCulture));
        _writer.Write("; lifecycle/configuration/strategy `");
        _writer.Write(record.Lifecycle.ToString(CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.Configuration.ToString(CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.Strategy.ToString(CultureInfo.InvariantCulture));
        _writer.Write('`');
        WriteRange("cycle", record.FirstCycle, record.LastCycle);
        _writer.Write("; start ");
        WriteCode(DecisionJournalValueNames.Decision(record.StartDecisionCode), record.StartDecisionCode);
        _writer.Write("; capture decision ");
        WriteCode(DecisionJournalValueNames.Decision(record.CaptureDecisionCode), record.CaptureDecisionCode);
        _writer.Write("; wake ");
        WriteWake(in record);
        _writer.Write("; projection ");
        WriteProjection(in record);
        _writer.Write("; fault ");
        WriteFault(in record);
        _writer.Write("; terminal ");
        WriteTerminal(in record);
    }

    private void WriteTransition(in DecisionJournalRecord record)
    {
        if (record.Service.IsValid)
        {
            _writer.Write("service `");
            _writer.Write(record.Service.Value.ToString(CultureInfo.InvariantCulture));
            _writer.Write("` ");
        }
        _writer.Write(record.Kind);
        switch (record.Kind)
        {
            case DecisionJournalRecordKind.ConfigurationChanged:
                WriteGeneration(record.Configuration);
                break;
            case DecisionJournalRecordKind.StrategyChanged:
                WriteGeneration(record.Strategy);
                break;
            case DecisionJournalRecordKind.LifecycleChanged:
                WriteGeneration(record.Lifecycle);
                _writer.Write("; state ");
                WriteCode(DecisionJournalValueNames.Lifecycle(record.TransitionCode), record.TransitionCode);
                break;
            case DecisionJournalRecordKind.WorldGateHeld:
                WriteGeneration(record.Lifecycle);
                _writer.Write("; waiting on ");
                WriteCode(DecisionJournalValueNames.WorldGate(record.TransitionCode), record.TransitionCode);
                break;
            case DecisionJournalRecordKind.EmergencyEntered:
            case DecisionJournalRecordKind.EmergencyCleared:
                _writer.Write("; reason ");
                WriteCode(DecisionJournalValueNames.Emergency(record.TransitionCode), record.TransitionCode);
                break;
        }
    }

    private void WriteGeneration(ulong generation)
    {
        _writer.Write("; generation `");
        _writer.Write(generation.ToString(CultureInfo.InvariantCulture));
        _writer.Write('`');
    }

    private void WriteRange(string name, ulong first, ulong last)
    {
        _writer.Write("; ");
        _writer.Write(name);
        _writer.Write(" `");
        if (first == 0)
        {
            _writer.Write("Unavailable");
        }
        else
        {
            _writer.Write(first.ToString(CultureInfo.InvariantCulture));
            if (last != first)
            {
                _writer.Write("..");
                _writer.Write(last.ToString(CultureInfo.InvariantCulture));
            }
        }
        _writer.Write('`');
    }

    private void WriteWake(in DecisionJournalRecord record)
    {
        if (!record.HasWake)
        {
            _writer.Write("Unavailable");
            return;
        }
        _writer.Write(record.Wake.Kind);
        if (record.Wake.Kind is WakePolicyKind.AfterDecision or WakePolicyKind.AfterBatch)
        {
            _writer.Write(' ');
            _writer.Write(TraceMetric.ToMilliseconds(record.Wake.Delay.Ticks)
                .ToString("F3", CultureInfo.InvariantCulture));
            _writer.Write(" ms");
        }
        else if (record.Wake.Kind == WakePolicyKind.At)
        {
            _writer.Write(" tick ");
            _writer.Write(record.Wake.DueTime.Ticks.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteProjection(in DecisionJournalRecord record)
    {
        if (!record.HasProjection)
        {
            _writer.Write("Unavailable");
            return;
        }
        _writer.Write('{');
        for (var index = 0; index < record.Projection.Count; index++)
        {
            if (index != 0) _writer.Write(", ");
            var entry = record.Projection.GetEntry(index);
            _writer.Write(entry.Key.Value.ToString(CultureInfo.InvariantCulture));
            _writer.Write('=');
            switch (entry.Value.Kind)
            {
                case ServiceProjectionValueKind.Boolean:
                    _writer.Write(entry.Value.Boolean ? "true" : "false");
                    break;
                case ServiceProjectionValueKind.Integer:
                    _writer.Write(entry.Value.Integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case ServiceProjectionValueKind.FloatingPoint:
                    _writer.Write(entry.Value.FloatingPoint.ToString("R", CultureInfo.InvariantCulture));
                    break;
            }
        }
        _writer.Write('}');
    }

    private void WriteFault(in DecisionJournalRecord record)
    {
        if (record.FaultCategory == 0)
        {
            _writer.Write("None");
            return;
        }
        _writer.Write(record.FaultCategory);
        _writer.Write('/');
        WriteCode(DecisionJournalValueNames.Action(record.FaultCode), record.FaultCode);
        _writer.Write(" occurrences ");
        _writer.Write(record.FirstFaultOccurrence.ToString(CultureInfo.InvariantCulture));
        if (record.LastFaultOccurrence != record.FirstFaultOccurrence)
        {
            _writer.Write("..");
            _writer.Write(record.LastFaultOccurrence.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteTerminal(in DecisionJournalRecord record)
    {
        if (record.TerminalDisposition == 0)
        {
            _writer.Write("Unavailable");
            return;
        }
        _writer.Write(record.TerminalDisposition);
        _writer.Write('/');
        WriteCode(DecisionJournalValueNames.Action(record.TerminalResultCode), record.TerminalResultCode);
        _writer.Write("; actions each/committed `");
        _writer.Write(record.ActionCount.ToString(CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.CommittedActions.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`; native/mutation/committed `");
        _writer.Write(record.NativeCallsAttempted.ToString(CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.MutationAttempts.ToString(CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.MutationsCommitted.ToString(CultureInfo.InvariantCulture));
        _writer.Write('`');
    }

    private void WriteCode(string name, int code)
    {
        _writer.Write(name);
        _writer.Write(" (`");
        _writer.Write(code.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`)");
    }
}
