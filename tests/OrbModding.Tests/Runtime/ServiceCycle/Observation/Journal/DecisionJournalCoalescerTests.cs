using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalObserverTestData;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalCoalescerTests
{
    [Fact]
    public void CheckpointPublishesOneSpanForEquivalentCycles()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        journal.Observe(CreateObservation(1, 1));
        journal.Observe(CreateObservation(2, 2));

        journal.Advance(new MonotonicTimestamp(10));

        var span = Assert.Single(sink.Records);
        Assert.Equal(2, span.RepeatCount);
        Assert.Equal(1, sink.FlushCount);
    }

    [Fact]
    public void DecisionOutcomeChangeClosesPriorSpanImmediately()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        journal.Observe(CreateObservation(1, 1));
        journal.Observe(CreateObservation(2, 2, faultOccurrence: 1));

        Assert.Single(sink.Records);
        journal.Stop(new MonotonicTimestamp(3));

        Assert.Equal(2, sink.Records.Count);
        Assert.Equal(1, sink.FlushCount);
        Assert.True(sink.Stopped);
    }

    [Fact]
    public void LowerFaultOccurrenceStartsANewSpan()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        journal.Observe(CreateObservation(1, 1, faultOccurrence: 2));
        journal.Observe(CreateObservation(2, 2, faultOccurrence: 1));

        Assert.Single(sink.Records);
        journal.Stop(new MonotonicTimestamp(3));

        Assert.Equal(2, sink.Records.Count);
        Assert.Equal(2, sink.Records[0].LastFaultOccurrence);
        Assert.Equal(1, sink.Records[1].FirstFaultOccurrence);
    }

    [Fact]
    public void ServiceTransitionClosesOnlyThatServicesSpan()
    {
        var sink = new RecordingSink();
        var journal = Create(sink, serviceCapacity: 2);
        journal.Observe(CreateObservation(1, 1, serviceValue: 1));
        journal.Observe(CreateObservation(1, 2, serviceValue: 2));
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            new ServiceCycleTraceServiceId(1),
            2,
            new MonotonicTimestamp(3));

        journal.ObserveTransition(in transition);

        Assert.Collection(
            sink.Records,
            span => Assert.Equal(new ServiceCycleTraceServiceId(1), span.Service),
            item => Assert.Equal(DecisionJournalRecordKind.LifecycleChanged, item.Kind));
        journal.Stop(new MonotonicTimestamp(4));
        Assert.Equal(new ServiceCycleTraceServiceId(2), sink.Records[2].Service);
    }

    [Fact]
    public void GlobalTransitionClosesEveryServiceBeforeItself()
    {
        var sink = new RecordingSink();
        var journal = Create(sink, serviceCapacity: 2);
        journal.Observe(CreateObservation(1, 1, serviceValue: 1));
        journal.Observe(CreateObservation(1, 2, serviceValue: 2));
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyEntered,
            default,
            0,
            new MonotonicTimestamp(3),
            1);

        journal.ObserveTransition(in transition);

        Assert.Equal(3, sink.Records.Count);
        Assert.Equal(DecisionJournalRecordKind.EmergencyEntered, sink.Records[2].Kind);
    }

    [Fact]
    public void BreakServiceSpanClearsActionCycleSuppression()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        var dispatch = CompletedAction(1);
        var fact = dispatch.ActionFact;
        var attribution = dispatch.Attribution;
        var action = new DecisionJournalActionObservation(
            new ServiceCycleTraceServiceId(1),
            in fact,
            in attribution);
        journal.ObserveAction(in action);

        journal.BreakServiceSpan(new ServiceCycleTraceServiceId(1), new MonotonicTimestamp(46));
        journal.Observe(CreateObservation(1, 47));
        journal.Stop(new MonotonicTimestamp(49));

        Assert.Equal(2, sink.Records.Count);
        Assert.Equal(DecisionJournalRecordKind.DecisionSpan, sink.Records[1].Kind);
    }

    [Fact]
    public void LifecycleTransitionClearsActionCycleSuppression()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        var dispatch = CompletedAction(1);
        var fact = dispatch.ActionFact;
        var attribution = dispatch.Attribution;
        var action = new DecisionJournalActionObservation(
            new ServiceCycleTraceServiceId(1),
            in fact,
            in attribution);
        journal.ObserveAction(in action);
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            new ServiceCycleTraceServiceId(1),
            2,
            new MonotonicTimestamp(46));

        journal.ObserveTransition(in transition);
        journal.Observe(CreateObservation(1, 47));
        journal.Stop(new MonotonicTimestamp(49));

        Assert.Equal(3, sink.Records.Count);
        Assert.Equal(DecisionJournalRecordKind.DecisionSpan, sink.Records[2].Kind);
    }

    [Fact]
    public void SinkFailureLatchesWithoutFallbackWrites()
    {
        var sink = new RecordingSink { AcceptWrites = false };
        var journal = Create(sink);
        journal.Observe(CreateObservation(1, 1));

        journal.Advance(new MonotonicTimestamp(10));
        journal.Advance(new MonotonicTimestamp(20));

        Assert.True(journal.IsFaulted);
        Assert.Equal(1, sink.AppendAttempts);
        Assert.Equal(0, sink.FlushCount);
    }

    [Fact]
    public void ExplicitFlushPublishesOpenPastEvidenceWithoutStoppingTheJournal()
    {
        var sink = new RecordingSink();
        var journal = Create(sink);
        journal.Observe(CreateObservation(1, 1));

        journal.Flush(new MonotonicTimestamp(2));

        Assert.Single(sink.Records);
        Assert.Equal(1, sink.FlushCount);
        Assert.False(sink.Stopped);
        journal.Observe(CreateObservation(2, 3));
        journal.Stop(new MonotonicTimestamp(4));
        Assert.Equal(2, sink.Records.Count);
    }

    private static DecisionJournalCoalescer Create(RecordingSink sink, int serviceCapacity = 1) =>
        new(
            serviceCapacity,
            sink,
            new MonotonicDuration(10),
            default);

    private sealed class RecordingSink : IDecisionJournalRecordSink
    {
        internal List<DecisionJournalRecord> Records { get; } = new();
        internal bool AcceptWrites { get; set; } = true;
        internal int AppendAttempts { get; private set; }
        internal int FlushCount { get; private set; }
        internal bool Stopped { get; private set; }

        public bool TryAppend(in DecisionJournalRecord record)
        {
            AppendAttempts++;
            if (!AcceptWrites) return false;
            Records.Add(record);
            return true;
        }

        public bool TryFlush()
        {
            FlushCount++;
            return AcceptWrites;
        }

        public void Stop() => Stopped = true;
    }
}
