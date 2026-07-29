using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalObserverTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class ServiceCycleDecisionJournalObserverTests
{
    [Fact]
    public void UnavailableCaptureKeepsReturnedWakeWithoutInventingStrategy()
    {
        var fixture = new Fixture();
        var attempt = CaptureUnavailable(1);

        fixture.Observer.StartAttemptObserved(0, in attempt, new MonotonicTimestamp(30));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        var record = Assert.Single(fixture.Sink.Records);
        Assert.Equal((ulong)1, record.FirstCycle);
        Assert.Equal((ulong)0, record.Strategy);
        Assert.Equal(CommonServiceDecisionCodes.CaptureUnavailable.Value, record.CaptureDecisionCode);
        Assert.True(record.HasWake);
        Assert.Equal(WakePolicyKind.AfterDecision, record.Wake.Kind);
    }

    /// <summary>
    /// A captured cycle records the strategy generation the runtime stamped it with.
    /// </summary>
    /// <remarks>
    /// The journal reads it off the capture context rather than the capture's return value, because
    /// the runtime pins it and the service never sees it. See W49.
    /// </remarks>
    [Fact]
    public void ACapturedCycleRecordsTheStrategyGenerationItRanAgainst()
    {
        var fixture = new Fixture();
        var attempt = CapturedAttempt(1, strategyValue: 7);

        fixture.Observer.StartAttemptObserved(0, in attempt, new MonotonicTimestamp(30));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        var record = Assert.Single(fixture.Sink.Records);
        Assert.Equal((ulong)7, record.Strategy);
    }

    /// <summary>
    /// The runtime pins a strategy generation for every capture attempt, but only one that produced a
    /// cycle moves the journal's own view of it.
    /// </summary>
    /// <remarks>
    /// Advancing here would journal a strategy change at a moment when nothing consumed the bulletin.
    /// The pump's evidence scanner reports the publication on its own account, so the change is not
    /// lost — it is just not this attempt's news.
    /// </remarks>
    [Fact]
    public void AnUnavailableCaptureDoesNotAdvanceTheJournalsStrategy()
    {
        var fixture = new Fixture();
        var attempt = CaptureUnavailable(1, strategyValue: 7);

        fixture.Observer.StartAttemptObserved(0, in attempt, new MonotonicTimestamp(30));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.DoesNotContain(
            fixture.Sink.Records,
            record => record.Kind == DecisionJournalRecordKind.StrategyChanged);
    }

    [Fact]
    public void DeferredPublicationMergesWithOriginalCapture()
    {
        var fixture = new Fixture();
        var capture = CapturedAttempt(1, queued: false);
        var queued = DeferredPublication(1);
        var response = SuccessfulResponse(1, actionCount: 0);

        fixture.Observer.StartAttemptObserved(0, in capture, new MonotonicTimestamp(20));
        fixture.Observer.StartAttemptObserved(0, in queued, new MonotonicTimestamp(21));
        fixture.Observer.ResponseAcquired(0, in response, new MonotonicTimestamp(32));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        var record = Assert.Single(fixture.Sink.Records);
        Assert.Equal(1, record.RepeatCount);
        Assert.Equal((ulong)1, record.FirstCycle);
        Assert.Equal(CommonServiceDecisionCodes.Captured.Value, record.CaptureDecisionCode);
        Assert.Equal(BatchTerminalDisposition.Completed, record.TerminalDisposition);
    }

    [Fact]
    public void ActionCycleJoinsProjectionWakeAndTerminalTotals()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1);
        var response = SuccessfulResponse(1, actionCount: 1);
        var terminal = CompletedAction(1);

        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in response, new MonotonicTimestamp(32));
        fixture.Observer.ActionDispatched(0, in terminal, new MonotonicTimestamp(46));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        var record = Assert.Single(fixture.Sink.Records);
        Assert.True(record.HasProjection);
        Assert.True(record.HasWake);
        Assert.Equal(WakePolicyKind.AfterBatch, record.Wake.Kind);
        Assert.Equal(1, record.ActionCount);
        Assert.Equal(1, record.CommittedActions);
        Assert.Equal(1, record.NativeCallsAttempted);
        Assert.Equal(1, record.MutationsCommitted);
    }

    /// <summary>
    /// A batch whose only action committed by publishing is journalled with the zero native totals it
    /// truthfully reports.
    /// </summary>
    /// <remarks>
    /// This is the record that killed the journal on the world collector's first completed cycle of
    /// every session it ever ran. The record dropped the receipt's published count, so the terminal
    /// validator could only read the batch's one action as one that owed a native mutation and
    /// produced none, and refused it. Nothing else in the suite put a publishing receipt and the
    /// journal in the same room.
    /// </remarks>
    [Fact]
    public void APublishingBatchIsJournalledWithoutOwingNativeEvidence()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1);
        var response = SuccessfulResponse(1, actionCount: 1);
        var terminal = PublishedAction(1);

        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in response, new MonotonicTimestamp(32));
        fixture.Observer.ActionDispatched(0, in terminal, new MonotonicTimestamp(46));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.False(fixture.Observer.IsFaulted);
        var record = Assert.Single(fixture.Sink.Records);
        Assert.Equal(BatchTerminalDisposition.Completed, record.TerminalDisposition);
        Assert.Equal(1, record.ActionCount);
        Assert.Equal(1, record.CommittedActions);
        Assert.Equal(1, record.PublishedActions);
        Assert.Equal(0, record.NativeCallsAttempted);
        Assert.Equal(0, record.MutationAttempts);
        Assert.Equal(0, record.MutationsCommitted);
    }

    /// <summary>
    /// A batch that published, mutated, and skipped keeps the two kinds of action apart.
    /// </summary>
    /// <remarks>
    /// The mutating half still owes one attempted mutation each and committed evidence for the one it
    /// committed; the published half owes none. Subtracting the published total is what lets the span
    /// keep both claims at once.
    /// </remarks>
    [Fact]
    public void AMixedBatchSeparatesWhatPublishedFromWhatTouchedTheGame()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1);
        var response = SuccessfulResponse(1, actionCount: 3);
        var terminal = MixedAction(1);

        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in response, new MonotonicTimestamp(32));
        fixture.Observer.ActionDispatched(0, in terminal, new MonotonicTimestamp(46));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.False(fixture.Observer.IsFaulted);
        var record = Assert.Single(fixture.Sink.Records);
        Assert.Equal(3, record.ActionCount);
        Assert.Equal(2, record.CommittedActions);
        Assert.Equal(1, record.PublishedActions);
        Assert.Equal(2, record.NativeCallsAttempted);
        Assert.Equal(2, record.MutationAttempts);
        Assert.Equal(1, record.MutationsCommitted);
    }

    [Fact]
    public void FaultStatePersistsUntilExactRecovery()
    {
        var fixture = new Fixture();
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(29));
        var firstStart = CapturedAttempt(1);
        var failed = FailedResponse(1, fault);
        var recovery = new ServiceFaultRecoveryFact(fault, new MonotonicTimestamp(50));
        var secondStart = CapturedAttempt(2, recovery: recovery);
        var completed = SuccessfulResponse(2, actionCount: 0);

        fixture.Observer.StartAttemptObserved(0, in firstStart, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in failed, new MonotonicTimestamp(32));
        fixture.Observer.StartAttemptObserved(0, in secondStart, new MonotonicTimestamp(50));
        fixture.Observer.ResponseAcquired(0, in completed, new MonotonicTimestamp(60));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.Collection(
            fixture.Sink.Records,
            first =>
            {
                Assert.Equal(ServiceFaultCategory.Evaluation, first.FaultCategory);
                Assert.False(first.HasWake);
            },
            second => Assert.Equal((ServiceFaultCategory)0, second.FaultCategory));
    }

    [Fact]
    public void RecoveryBreaksSpanBeforeSameFaultRecurs()
    {
        var fixture = new Fixture();
        var firstFault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(29));
        var repeatedFault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(59));
        var recovery = new ServiceFaultRecoveryFact(firstFault, new MonotonicTimestamp(50));

        var firstStart = CapturedAttempt(1);
        var firstResponse = FailedResponse(1, firstFault);
        fixture.Observer.StartAttemptObserved(0, in firstStart, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in firstResponse, new MonotonicTimestamp(32));

        var secondStart = CapturedAttempt(2, recovery: recovery);
        var secondResponse = FailedResponse(2, repeatedFault);
        fixture.Observer.StartAttemptObserved(0, in secondStart, new MonotonicTimestamp(50));
        fixture.Observer.ResponseAcquired(0, in secondResponse, new MonotonicTimestamp(62));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.Equal(2, fixture.Sink.Records.Count);
        Assert.All(fixture.Sink.Records, record => Assert.Equal(1, record.FirstFaultOccurrence));
    }

    [Fact]
    public void RecoveryForMaskedFaultPreservesCurrentVisibleFault()
    {
        var visible = new ServiceFault(
            ServiceFaultCategory.Capture,
            CommonActionResultCodes.AdapterFault,
            2,
            new MonotonicTimestamp(29));
        var masked = new ServiceFault(
            ServiceFaultCategory.ActionExecution,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(20));
        var cursor = new DecisionJournalServiceCursor();
        cursor.Bind(
            new ServiceCycleTraceServiceId(1),
            new LifecycleGeneration(1),
            visible,
            lifecycleSemanticVersion: 1,
            lifecycleTerminalSequence: 0,
            constructionDeferralSequence: 0,
            worldGateDeferralSequence: 0);

        cursor.ObserveFaultTransition(
            new ServiceFaultRecoveryFact(masked, new MonotonicTimestamp(30)),
            default);
        var start = CapturedAttempt(1);
        cursor.BeginCycle(in start, new MonotonicTimestamp(31));
        var observation = cursor.CompleteWithoutTerminal(new MonotonicTimestamp(32));

        Assert.Equal(ServiceFaultCategory.Capture, observation.Fault.Category);
        Assert.Equal(2, observation.Fault.OccurrenceCount);
    }

    [Fact]
    public void EmergencyTransitionClosesOnlyUnqueuedCaptureAndCarriesReason()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1, queued: false);
        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(1),
            new EmergencyStopTransitionGeneration(1),
            EmergencyStopReason.SafetyInterlock);

        fixture.Observer.EmergencyEntered(in emergency, new MonotonicTimestamp(30));

        Assert.Collection(
            fixture.Sink.Records,
            decision =>
            {
                Assert.Equal(DecisionJournalRecordKind.DecisionSpan, decision.Kind);
                Assert.Equal((BatchTerminalDisposition)0, decision.TerminalDisposition);
            },
            transition =>
            {
                Assert.Equal(DecisionJournalRecordKind.EmergencyEntered, transition.Kind);
                Assert.Equal((int)EmergencyStopReason.SafetyInterlock, transition.TransitionCode);
            });
    }

    /// <summary>
    /// A contained failure names the observation it happened in and what was violated.
    /// </summary>
    /// <remarks>
    /// Containing the exception without keeping it left the runtime able to report only that the
    /// producer failed, which is the same sentence for every one of these call sites. The first
    /// failure is the one kept: everything after it is a consequence of a journal that already
    /// stopped tracking the suite.
    /// </remarks>
    [Fact]
    public void MismatchedResponseFaultsOnlyTheObserverAndNamesTheGuardItViolated()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1);
        var wrong = SuccessfulResponse(2, actionCount: 0);

        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in wrong, new MonotonicTimestamp(32));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.True(fixture.Observer.IsFaulted);
        Assert.Empty(fixture.Sink.Records);
        Assert.Equal("ResponseAcquired", fixture.Observer.FaultSite);
        Assert.Equal(
            "Journal facts do not match the pending service cycle.",
            Assert.IsType<InvalidOperationException>(fixture.Observer.FaultException).Message);
    }

    [Fact]
    public void AJournalThatOutlivesItsSinkStillNamesWhereItDied()
    {
        var sink = new RefusingSink();
        var journal = new DecisionJournalCoalescer(1, sink, new MonotonicDuration(100), default);
        var observer = new ServiceCycleDecisionJournalObserver(journal, 1);
        observer.Bind(
            0,
            new LifecycleGeneration(1),
            default,
            lifecycleSemanticVersion: 1,
            lifecycleTerminalSequence: 0,
            constructionDeferralSequence: 0,
            worldGateDeferralSequence: 0);
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(1),
            new EmergencyStopTransitionGeneration(1),
            EmergencyStopReason.SafetyInterlock);

        observer.EmergencyEntered(in emergency, new MonotonicTimestamp(30));

        Assert.True(observer.IsFaulted);
        Assert.Null(observer.FaultException);
        Assert.Equal("DecisionJournalCoalescer", observer.FaultSite);
    }

    [Fact]
    public void BindSeedsRetainedLifecycleFactSequences()
    {
        var fixture = new Fixture(lifecycleTerminalSequence: 7, constructionDeferralSequence: 8);
        var terminal = new ServiceLifecycleTerminalFact(
            sequence: 7,
            new LifecycleGeneration(1),
            new LifecycleGeneration(1),
            ServiceCyclePhase.Waiting,
            default,
            default,
            default,
            default,
            new MonotonicTimestamp(20));
        var deferral = new ServiceLifecycleConstructionDeferralFact(
            sequence: 8,
            new LifecycleGeneration(1),
            new MonotonicTimestamp(21),
            new MonotonicTimestamp(30));
        var snapshot = new ServiceLifecycleSlotSnapshot(
            new LifecycleGeneration(1),
            new ServiceRunnerPositionSnapshot(
                0,
                ServiceRunnerPositionState.Current,
                new LifecycleGeneration(1),
                ServiceHandoffPhase.Empty,
                default),
            default,
            terminal,
            deferral,
            default,
            default,
            default,
            constructionAttemptCount: 1,
            constructionContentionCount: 1);

        fixture.Observer.ObserveLifecycle(
            0,
            in snapshot,
            lifecycleSemanticVersion: 2,
            new MonotonicTimestamp(40));

        Assert.False(fixture.Observer.IsFaulted);
        Assert.Empty(fixture.Sink.Records);
    }

    [Fact]
    public void LifecycleRetirementCompletesPendingCycleBeforeActivation()
    {
        var fixture = new Fixture();
        var start = CapturedAttempt(1);
        var response = SuccessfulResponse(1, actionCount: 1);
        fixture.Observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        fixture.Observer.LifecycleRequested(
            0,
            new LifecycleGeneration(2),
            new MonotonicTimestamp(40));
        var cycle = DecisionJournalTestData.Identity(1);
        var receipt = BatchReceipt.Orphaned(
            cycle,
            new BatchId(1),
            actionCount: 1,
            committedCount: 0,
            default,
            new MonotonicTimestamp(45));
        var terminal = new ServiceLifecycleTerminalFact(
            sequence: 1,
            new LifecycleGeneration(1),
            new LifecycleGeneration(2),
            ServiceCyclePhase.Executing,
            cycle,
            new BatchId(1),
            response.Response,
            receipt,
            new MonotonicTimestamp(45));
        var snapshot = new ServiceLifecycleSlotSnapshot(
            new LifecycleGeneration(2),
            new ServiceRunnerPositionSnapshot(
                0,
                ServiceRunnerPositionState.Retiring,
                new LifecycleGeneration(1),
                ServiceHandoffPhase.Stopping,
                default),
            new ServiceRunnerPositionSnapshot(
                1,
                ServiceRunnerPositionState.Current,
                new LifecycleGeneration(2),
                ServiceHandoffPhase.Empty,
                default),
            terminal,
            default,
            default,
            default,
            default,
            constructionAttemptCount: 1,
            constructionContentionCount: 0);

        Assert.True(fixture.Observer.NeedsLifecycleObservation(0, lifecycleSemanticVersion: 2));
        fixture.Observer.ObserveLifecycle(
            0,
            in snapshot,
            lifecycleSemanticVersion: 2,
            new MonotonicTimestamp(50));

        Assert.False(fixture.Observer.NeedsLifecycleObservation(0, lifecycleSemanticVersion: 2));
        Assert.Collection(
            fixture.Sink.Records,
            requested =>
            {
                Assert.Equal(DecisionJournalRecordKind.LifecycleChanged, requested.Kind);
                Assert.Equal(1, requested.TransitionCode);
            },
            decision =>
            {
                Assert.True(decision.HasProjection);
                Assert.Equal(BatchTerminalDisposition.Orphaned, decision.TerminalDisposition);
            },
            activated =>
            {
                Assert.Equal(DecisionJournalRecordKind.LifecycleChanged, activated.Kind);
                Assert.Equal(2, activated.TransitionCode);
            });
    }

    [Fact]
    public void LifecycleActivationClearsRetiredRunnerFault()
    {
        var fixture = new Fixture();
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(29));
        var firstStart = CapturedAttempt(1);
        var failed = FailedResponse(1, fault);
        fixture.Observer.StartAttemptObserved(0, in firstStart, new MonotonicTimestamp(20));
        fixture.Observer.ResponseAcquired(0, in failed, new MonotonicTimestamp(32));
        fixture.Observer.LifecycleRequested(
            0,
            new LifecycleGeneration(2),
            new MonotonicTimestamp(40));
        var snapshot = new ServiceLifecycleSlotSnapshot(
            new LifecycleGeneration(2),
            default,
            new ServiceRunnerPositionSnapshot(
                1,
                ServiceRunnerPositionState.Current,
                new LifecycleGeneration(2),
                ServiceHandoffPhase.Empty,
                default),
            default,
            default,
            default,
            default,
            default,
            constructionAttemptCount: 1,
            constructionContentionCount: 0);
        fixture.Observer.ObserveLifecycle(
            0,
            in snapshot,
            lifecycleSemanticVersion: 2,
            new MonotonicTimestamp(50));

        var secondStart = CapturedAttempt(2, lifecycleValue: 2);
        var completed = SuccessfulResponse(2, actionCount: 0, lifecycleValue: 2);
        fixture.Observer.StartAttemptObserved(0, in secondStart, new MonotonicTimestamp(60));
        fixture.Observer.ResponseAcquired(0, in completed, new MonotonicTimestamp(70));
        fixture.Observer.Advance(new MonotonicTimestamp(100));

        Assert.Equal(4, fixture.Sink.Records.Count);
        Assert.Equal(ServiceFaultCategory.Evaluation, fixture.Sink.Records[0].FaultCategory);
        Assert.Equal((ServiceFaultCategory)0, fixture.Sink.Records[3].FaultCategory);
        Assert.Equal((ulong)2, fixture.Sink.Records[3].Lifecycle);
    }

    /// <summary>
    /// A publication is one journal record however many services are registered.
    /// </summary>
    /// <remarks>
    /// Three services meant three identical `ConfigurationChanged` and three identical
    /// `StrategyChanged` records for one publication. The record now names the suite, and being
    /// suite-wide it breaks every open span rather than only one service's.
    /// </remarks>
    [Fact]
    public void OnePublicationIsOneRecordWhateverTheServiceCount()
    {
        var sink = new RecordingSink();
        var journal = new DecisionJournalCoalescer(3, sink, new MonotonicDuration(1000), default);
        var observer = new ServiceCycleDecisionJournalObserver(journal, 3);
        observer.BindPublications(new ConfigGeneration(1), new StrategyGeneration(1));
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            observer.Bind(
                ordinal,
                new LifecycleGeneration(1),
                default,
                lifecycleSemanticVersion: 1,
                lifecycleTerminalSequence: 0,
                constructionDeferralSequence: 0,
                worldGateDeferralSequence: 0);
        }

        observer.ObservePublications(
            new ConfigGeneration(2),
            new StrategyGeneration(2),
            new MonotonicTimestamp(10));
        observer.ObservePublications(
            new ConfigGeneration(2),
            new StrategyGeneration(2),
            new MonotonicTimestamp(11));

        Assert.Collection(
            sink.Records,
            record =>
            {
                Assert.Equal(DecisionJournalRecordKind.ConfigurationChanged, record.Kind);
                Assert.Equal((ulong)2, record.Configuration);
                Assert.False(record.Service.IsValid);
            },
            record =>
            {
                Assert.Equal(DecisionJournalRecordKind.StrategyChanged, record.Kind);
                Assert.Equal((ulong)2, record.Strategy);
                Assert.False(record.Service.IsValid);
            });
    }

    /// <summary>
    /// A service the world gate holds is one record for as long as the hold means the same thing.
    /// </summary>
    /// <remarks>
    /// The gate re-defers a held service every frame, so recording each deferral would fill the
    /// journal with the same fact while the suite sat still. What is worth a record is the hold
    /// opening, the reason changing, and the service acting again and being held afresh.
    /// </remarks>
    [Fact]
    public void AWorldGateHoldIsOneRecordUntilWhatItWaitsOnChanges()
    {
        var fixture = new Fixture();

        for (var frame = 1L; frame <= 3; frame++)
        {
            var behind = new ServiceWorldGateDeferralFact(frame, frame, 10, new WorldGeneration(9));
            fixture.Observer.ObserveWorldGate(0, in behind, new MonotonicTimestamp(20 + frame));
        }
        var unanswered = new ServiceWorldGateDeferralFact(4, 4, 10, default);
        fixture.Observer.ObserveWorldGate(0, in unanswered, new MonotonicTimestamp(30));
        var afterActingAgain = new ServiceWorldGateDeferralFact(5, 5, 12, new WorldGeneration(11));
        fixture.Observer.ObserveWorldGate(0, in afterActingAgain, new MonotonicTimestamp(40));

        Assert.Collection(
            fixture.Sink.Records,
            record =>
            {
                Assert.Equal(DecisionJournalRecordKind.WorldGateHeld, record.Kind);
                Assert.True(record.Service.IsValid);
                Assert.Equal((ulong)1, record.Lifecycle);
                Assert.Equal(DecisionJournalServiceCursor.WorldBehindLastActionCode, record.TransitionCode);
                Assert.Equal(21, record.FirstTimestampTicks);
            },
            record => Assert.Equal(
                DecisionJournalServiceCursor.WorldUnansweredCode,
                record.TransitionCode),
            record => Assert.Equal(
                DecisionJournalServiceCursor.WorldBehindLastActionCode,
                record.TransitionCode));
    }

    [Fact]
    public void BindSeedsTheWorldGateSequenceSoAHoldAlreadyInForceIsNotRestated()
    {
        var fixture = new Fixture(worldGateDeferralSequence: 4);
        var stale = new ServiceWorldGateDeferralFact(4, 4, 10, new WorldGeneration(9));

        fixture.Observer.ObserveWorldGate(0, in stale, new MonotonicTimestamp(20));

        Assert.Empty(fixture.Sink.Records);
    }

    private sealed class Fixture
    {
        internal Fixture(
            long lifecycleTerminalSequence = 0,
            long constructionDeferralSequence = 0,
            long worldGateDeferralSequence = 0)
        {
            var journal = new DecisionJournalCoalescer(
                1,
                Sink,
                new MonotonicDuration(100),
                default);
            Observer = new ServiceCycleDecisionJournalObserver(journal, 1);
            Observer.BindPublications(new ConfigGeneration(1), new StrategyGeneration(1));
            Observer.Bind(
                0,
                new LifecycleGeneration(1),
                default,
                lifecycleSemanticVersion: 1,
                lifecycleTerminalSequence,
                constructionDeferralSequence,
                worldGateDeferralSequence);
        }

        internal RecordingSink Sink { get; } = new();
        internal ServiceCycleDecisionJournalObserver Observer { get; }
    }

    private sealed class RefusingSink : IDecisionJournalRecordSink
    {
        public bool TryAppend(in DecisionJournalRecord record) => false;
        public bool TryFlush() => false;
        public void Stop() { }
    }

    private sealed class RecordingSink : IDecisionJournalRecordSink
    {
        internal List<DecisionJournalRecord> Records { get; } = new();

        public bool TryAppend(in DecisionJournalRecord record)
        {
            Records.Add(record);
            return true;
        }

        public bool TryFlush() => true;
        public void Stop() { }
    }
}
