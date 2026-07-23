#if SERVICE_CYCLE_PROFILE
using System.Globalization;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.Journal;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.ServiceCycleTrace.Performance;

namespace OrbModding.ServiceCycleTrace.Dashboard;

internal static class TraceDashboardReader
{
    private const double TicksPerMillisecond = TimeSpan.TicksPerMillisecond;

    internal static TraceDashboardDocument Read(string captureDirectory)
    {
        var root = Path.GetFullPath(captureDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The correlated capture directory does not exist.");

        var full = ManualFullTraceSessionReader.Read(SingleSession(root, "full"));
        var profile = ServiceCycleProfileSessionReader.Read(SingleSession(root, "profile"));
        var journal = ReadLatestJournalRun(Path.Combine(root, "journal"));
        var firstTicks = full.Document.FirstTimestampTicks;
        var lastTicks = full.Document.LastTimestampTicks;
        if (lastTicks < firstTicks)
            throw new InvalidDataException("The full trace has an invalid observed window.");

        var pumps = new List<TraceDashboardPump>();
        var events = new List<TraceDashboardEvent>();
        foreach (var segment in full.Segments())
        {
            foreach (var item in segment.Events)
            {
                var payload = item.Payload;
                if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
                    pumps.Add(Pump(in payload, firstTicks));
                else
                    events.Add(Event(item.Kind, in payload, firstTicks));
            }
        }

        var decisions = Decisions(journal.Records, firstTicks, lastTicks);
        var aggregates = new List<TraceDashboardStageAggregate>();
        var samples = new List<TraceDashboardStageSample>();
        var calibration = profile.Manifest.Calibration;
        foreach (var record in profile.Records)
        {
            if (record.Kind == ServiceCycleProfileRecordKind.Aggregate)
                aggregates.Add(Aggregate(in record, in calibration));
            else
                samples.Add(Sample(in record, in calibration, firstTicks));
        }

        var metadata = new TraceDashboardMetadata(
            full.Name,
            full.Document.State.ToString(),
            full.Document.TerminalReason?.ToString() ?? "Unavailable",
            "session-" + profile.Manifest.Session.Value.ToString("x16", CultureInfo.InvariantCulture),
            profile.Manifest.Completeness.ToString(),
            profile.Manifest.Reason.ToString(),
            journal.Run.ToString("x16", CultureInfo.InvariantCulture),
            Milliseconds(lastTicks - firstTicks),
            full.Document.WrittenRecords,
            profile.Manifest.WrittenRecords,
            decisions.Length,
            calibration.TraceActive,
            calibration.AllocationAvailable);
        return new TraceDashboardDocument(
            1,
            metadata,
            pumps.ToArray(),
            events.ToArray(),
            decisions,
            aggregates.ToArray(),
            samples.ToArray());
    }

    private static string SingleSession(string root, string child)
    {
        var directory = Path.Combine(root, child);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The correlated capture has no '{child}' directory.");
        var sessions = Directory.GetDirectories(directory, "session-*", SearchOption.TopDirectoryOnly);
        if (sessions.Length != 1)
            throw new InvalidDataException($"The correlated capture must contain exactly one '{child}' session.");
        return sessions[0];
    }

    private static JournalRun ReadLatestJournalRun(string path)
    {
        var directory = new DecisionJournalDirectory(path);
        var inventory = directory.Inventory();
        if (!inventory.HasSegments)
            throw new InvalidDataException("The correlated capture has no decision-journal segments.");
        var latest = directory.ReadSegment(inventory.LastOrdinal, out _).Run.Value;
        var records = new List<DecisionJournalRecord>();
        var assembler = new DecisionJournalWindowAssembler();
        for (var ordinal = inventory.FirstOrdinal; ordinal <= inventory.LastOrdinal; ordinal++)
        {
            var segment = directory.ReadSegment(ordinal, out var encodedBytes);
            assembler.Add(segment, encodedBytes);
            if (segment.Run.Value == latest) records.AddRange(segment.Records);
        }
        var window = assembler.Complete();
        if (window.SegmentCount != checked((ulong)inventory.Count))
            throw new InvalidDataException("The decision-journal inventory changed while it was read.");
        return new JournalRun(latest, records.ToArray());
    }

    private static TraceDashboardPump Pump(in ServiceCycleSemanticPayload value, long origin) => new(
        Offset(value.TimestampTicks, origin),
        value.FrameIdentity,
        value.PumpAccepted,
        Milliseconds(value.TotalDurationTicks),
        Milliseconds(value.ResponseDurationTicks),
        Milliseconds(value.CaptureDurationTicks),
        Milliseconds(value.ActionDurationTicks),
        value.ResponsesAcquired,
        value.CapturesAttempted,
        value.ActionsAttempted,
        value.LifecycleTransitions);

    private static TraceDashboardEvent Event(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload value,
        long origin) => new(
        Offset(value.TimestampTicks, origin),
        kind.ToString(),
        Lane(kind),
        value.Service,
        value.Lifecycle,
        value.Capture,
        value.Cycle,
        value.Batch,
        value.Action,
        value.FrameIdentity,
        Milliseconds(value.DurationTicks),
        value.Code,
        value.Disposition,
        value.ActionCount,
        value.CommittedCount,
        value.NativeCallsAttempted,
        value.MutationAttempts,
        value.MutationsCommitted);

    private static TraceDashboardDecision[] Decisions(
        DecisionJournalRecord[] records,
        long firstTicks,
        long lastTicks)
    {
        var output = new List<TraceDashboardDecision>();
        foreach (var record in records)
        {
            if (record.Kind != DecisionJournalRecordKind.DecisionSpan ||
                record.LastTimestampTicks < firstTicks || record.FirstTimestampTicks > lastTicks)
                continue;
            output.Add(new TraceDashboardDecision(
                Offset(Math.Max(record.FirstTimestampTicks, firstTicks), firstTicks),
                Offset(Math.Min(record.LastTimestampTicks, lastTicks), firstTicks),
                record.Service.Value,
                record.FirstCycle,
                record.LastCycle,
                record.RepeatCount,
                DecisionJournalValueNames.Decision(record.StartDecisionCode),
                DecisionJournalValueNames.Decision(record.CaptureDecisionCode),
                Wake(in record),
                record.TerminalDisposition == 0
                    ? "Unavailable"
                    : record.TerminalDisposition + "/" + DecisionJournalValueNames.Action(record.TerminalResultCode),
                record.ActionCount,
                record.CommittedActions,
                record.NativeCallsAttempted,
                record.MutationAttempts,
                record.MutationsCommitted,
                Projection(in record)));
        }
        return output.ToArray();
    }

    private static TraceDashboardProjectionEntry[] Projection(in DecisionJournalRecord record)
    {
        if (!record.HasProjection) return Array.Empty<TraceDashboardProjectionEntry>();
        var output = new TraceDashboardProjectionEntry[record.Projection.Count];
        for (var index = 0; index < output.Length; index++)
        {
            var entry = record.Projection.GetEntry(index);
            output[index] = new TraceDashboardProjectionEntry(
                entry.Key.Value,
                ProjectionName(entry.Key.Value),
                entry.Value.Kind.ToString(),
                ProjectionValue(entry.Key.Value, entry.Value));
        }
        return output;
    }

    private static string ProjectionName(int key) => key switch
    {
        1 => "Next pair",
        2 => "Has planned action",
        3 => "Planned pair",
        4 => "Fruit selected",
        5 => "Fruit health",
        6 => "Fruit feature-scoped",
        7 => "Treasure selected",
        8 => "Treasure health",
        9 => "Treasure feature-scoped",
        _ => "Field " + key.ToString(CultureInfo.InvariantCulture),
    };

    private static string ProjectionValue(int key, ServiceProjectionValue value)
    {
        if (value.Kind == ServiceProjectionValueKind.Boolean) return value.Boolean ? "true" : "false";
        if (value.Kind == ServiceProjectionValueKind.FloatingPoint)
            return value.FloatingPoint.ToString("0.###", CultureInfo.InvariantCulture);
        if (value.Integer is >= int.MinValue and <= int.MaxValue)
        {
            var integer = (int)value.Integer;
            if (key is 1 or 3 && Enum.IsDefined(typeof(AutoHarvestPair), integer))
                return ((AutoHarvestPair)integer).ToString();
            if (key is 5 or 8 && Enum.IsDefined(typeof(AutoHarvestPairHealthKind), integer))
                return ((AutoHarvestPairHealthKind)integer).ToString();
        }
        return value.Integer.ToString(CultureInfo.InvariantCulture);
    }

    private static string Wake(in DecisionJournalRecord record)
    {
        if (!record.HasWake) return "Unavailable";
        return record.Wake.Kind switch
        {
            WakePolicyKind.AfterDecision or WakePolicyKind.AfterBatch =>
                record.Wake.Kind + " " + Milliseconds(record.Wake.Delay.Ticks).ToString("0.###", CultureInfo.InvariantCulture) + " ms",
            WakePolicyKind.At => "At " + record.Wake.DueTime.Ticks.ToString(CultureInfo.InvariantCulture),
            _ => record.Wake.Kind.ToString(),
        };
    }

    private static TraceDashboardStageAggregate Aggregate(
        in ServiceCycleProfileRecord record,
        in ServiceCycleProfileCalibration calibration)
    {
        var averageTicks = (double)record.TotalElapsedRawTicks / record.OccurrenceCount;
        var operations = record.Operations;
        return new TraceDashboardStageAggregate(
            record.StageCode,
            ServiceCycleProfileNames.Stage(record.StageCode),
            record.ServiceOrdinal,
            record.Temperature.ToString(),
            record.OccurrenceCount,
            Microseconds(record.TotalElapsedRawTicks, calibration.TimestampFrequency),
            Microseconds(averageTicks, calibration.TimestampFrequency),
            Microseconds(record.MinimumElapsedRawTicks, calibration.TimestampFrequency),
            Microseconds(record.MaximumElapsedRawTicks, calibration.TimestampFrequency),
            calibration.AllocationAvailable
                ? (double)record.TotalAllocatedBytes / record.OccurrenceCount
                : null,
            Operations(in operations));
    }

    private static TraceDashboardStageSample Sample(
        in ServiceCycleProfileRecord record,
        in ServiceCycleProfileCalibration calibration,
        long origin)
    {
        var timestamp = CalibratedTimestamp(record.FirstStartedAtRawTicks, in calibration);
        var operations = record.Operations;
        return new TraceDashboardStageSample(
            Offset(timestamp, origin),
            record.StageCode,
            ServiceCycleProfileNames.Stage(record.StageCode),
            record.ServiceOrdinal,
            record.Cycle,
            record.Frame,
            record.Temperature.ToString(),
            Microseconds(record.TotalElapsedRawTicks, calibration.TimestampFrequency),
            calibration.AllocationAvailable ? checked((long)record.TotalAllocatedBytes) : null,
            Operations(in operations));
    }

    private static TraceDashboardOperations Operations(in ServiceCycleProfileOperations value) => new(
        value.ReflectedFieldReads,
        value.ReflectedMethodCalls,
        value.StableIdReads,
        value.ListEntries,
        value.SelectedPairs,
        value.ReadyPairs,
        value.InvocationArgumentArrays,
        value.RecordCopies);

    private static long CalibratedTimestamp(long rawTimestamp, in ServiceCycleProfileCalibration calibration)
    {
        var delta = checked(rawTimestamp - calibration.RawTimestamp);
        var monotonicDelta = checked((long)decimal.Truncate(
            (decimal)delta * MonotonicDuration.TicksPerSecond / calibration.TimestampFrequency));
        return checked(calibration.MonotonicTimestampTicks + monotonicDelta);
    }

    private static string Lane(ServiceCycleSemanticEventKind kind) => kind switch
    {
        ServiceCycleSemanticEventKind.CaptureStarted or
        ServiceCycleSemanticEventKind.CaptureCompleted or
        ServiceCycleSemanticEventKind.CaptureUnavailable or
        ServiceCycleSemanticEventKind.CaptureFaulted => "Capture",
        ServiceCycleSemanticEventKind.CycleQueued or
        ServiceCycleSemanticEventKind.CycleStarted or
        ServiceCycleSemanticEventKind.CycleCompleted or
        ServiceCycleSemanticEventKind.CycleFaulted or
        ServiceCycleSemanticEventKind.CycleOrphaned or
        ServiceCycleSemanticEventKind.EvaluationStarted or
        ServiceCycleSemanticEventKind.EvaluationCompleted or
        ServiceCycleSemanticEventKind.EvaluationDeferred or
        ServiceCycleSemanticEventKind.EvaluationFaulted => "Worker",
        ServiceCycleSemanticEventKind.StatePublished or
        ServiceCycleSemanticEventKind.BatchPublished or
        ServiceCycleSemanticEventKind.BatchCompleted or
        ServiceCycleSemanticEventKind.BatchAborted or
        ServiceCycleSemanticEventKind.BatchOrphaned => "Publication",
        ServiceCycleSemanticEventKind.ActionAttempted or
        ServiceCycleSemanticEventKind.ActionCommitted or
        ServiceCycleSemanticEventKind.ActionRejected or
        ServiceCycleSemanticEventKind.ActionFaulted => "Action",
        ServiceCycleSemanticEventKind.LifecycleRequested or
        ServiceCycleSemanticEventKind.LifecycleActivated or
        ServiceCycleSemanticEventKind.LifecycleRetired or
        ServiceCycleSemanticEventKind.StartAttempted or
        ServiceCycleSemanticEventKind.StartDeferred or
        ServiceCycleSemanticEventKind.StartFaulted or
        ServiceCycleSemanticEventKind.StartReady => "Lifecycle",
        _ => "Control",
    };

    private static double Offset(long ticks, long origin) => Milliseconds(checked(ticks - origin));
    private static double Milliseconds(long ticks) => ticks / TicksPerMillisecond;
    private static double Microseconds(double ticks, long frequency) => ticks * 1_000_000d / frequency;

    private readonly record struct JournalRun(ulong Run, DecisionJournalRecord[] Records);
}
#endif
