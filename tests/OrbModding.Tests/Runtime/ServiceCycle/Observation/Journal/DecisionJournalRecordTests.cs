using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalRecordTests
{
    [Fact]
    public void EquivalentConsecutiveCyclesCoalesceIntoOneSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10, faultOccurrence: 1));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20, faultOccurrence: 2));

        var span = first.Coalesce(in second);

        Assert.Equal(2, span.RepeatCount);
        Assert.Equal((ulong)1, span.FirstCycle);
        Assert.Equal((ulong)2, span.LastCycle);
        Assert.Equal(10, span.FirstTimestampTicks);
        Assert.Equal(21, span.LastTimestampTicks);
        Assert.Equal(1, span.FirstFaultOccurrence);
        Assert.Equal(2, span.LastFaultOccurrence);
        Assert.Equal(2, span.CommittedActions);
        Assert.Equal(2, span.NativeCallsAttempted);
        Assert.Equal(2, span.MutationsCommitted);
    }

    /// <summary>
    /// Published actions sum across a span the way committed ones do.
    /// </summary>
    /// <remarks>
    /// Per-repeat, like the action count, would be wrong: two repeats that share an action count can
    /// publish different halves of it, and the span's native expectations are stated against the sum.
    /// A span of pure publications therefore keeps owing no native evidence however long it runs.
    /// </remarks>
    [Fact]
    public void ConsecutivePublicationsSumThePublishedActions()
    {
        var first = DecisionJournalRecord.Decision(PublicationObservation(1, 10));
        var second = DecisionJournalRecord.Decision(PublicationObservation(2, 20));

        var span = first.Coalesce(in second);

        Assert.Equal(2, span.RepeatCount);
        Assert.Equal(1, span.ActionCount);
        Assert.Equal(2, span.CommittedActions);
        Assert.Equal(2, span.PublishedActions);
        Assert.Equal(0, span.NativeCallsAttempted);
        Assert.Equal(0, span.MutationAttempts);
        Assert.Equal(0, span.MutationsCommitted);
    }

    [Fact]
    public void ProjectionChangeBreaksSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10, projectionValue: 7));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20, projectionValue: 8));

        Assert.False(first.CanCoalesceWith(in second));
    }

    [Fact]
    public void WakeChangeBreaksSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10, wakeTicks: 5));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20, wakeTicks: 6));

        Assert.False(first.CanCoalesceWith(in second));
    }

    [Fact]
    public void MissingCycleBreaksOtherwiseEquivalentSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var third = DecisionJournalRecord.Decision(CreateObservation(3, 20));

        Assert.False(first.CanCoalesceWith(in third));
    }

    /// <summary>
    /// Lifecycle names a service; configuration, strategy and emergency name the suite.
    /// </summary>
    /// <remarks>
    /// The suite publishes one configuration record and one strategy bulletin that every service
    /// reads, so a change is one record. Attributing it to a service produced N identical records
    /// and implied a per-service generation the runtime does not have.
    /// </remarks>
    [Fact]
    public void ServiceAndSuiteWideTransitionsHaveDistinctIdentityRules()
    {
        var service = new ServiceCycleTraceServiceId(1);
        var lifecycle = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            service,
            2,
            new MonotonicTimestamp(10));
        var configuration = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            2,
            new MonotonicTimestamp(15));
        var emergency = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyEntered,
            default,
            0,
            new MonotonicTimestamp(20),
            code: 1);

        var worldGate = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            service,
            2,
            new MonotonicTimestamp(25),
            code: 1);

        Assert.Equal((ulong)2, lifecycle.Lifecycle);
        Assert.Equal((ulong)2, worldGate.Lifecycle);
        Assert.True(worldGate.Service.IsValid);
        Assert.Equal((ulong)2, configuration.Configuration);
        Assert.False(configuration.Service.IsValid);
        Assert.Equal(DecisionJournalRecordKind.EmergencyEntered, emergency.Kind);
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            service,
            2,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.StrategyChanged,
            service,
            2,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyCleared,
            service,
            0,
            default));
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            0,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            default,
            2,
            default));
    }

    [Fact]
    public void UnavailableCaptureRetainsIdentityWithoutInventingStrategy()
    {
        var projection = default(ServiceStateProjectionSnapshot);
        var fault = default(ServiceFault);
        var terminal = default(BatchReceipt);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            lifecycle: 1,
            configuration: 1,
            strategy: 0,
            cycle: 4,
            default,
            default,
            CommonServiceDecisionCodes.Ready.Value,
            CommonServiceDecisionCodes.CaptureUnavailable.Value,
            true,
            WakePolicy.AfterDecision(new MonotonicDuration(5)),
            false,
            in projection,
            in fault,
            in terminal);

        var record = DecisionJournalRecord.Decision(in observation);

        Assert.Equal((ulong)4, record.FirstCycle);
        Assert.Equal((ulong)0, record.Strategy);
        Assert.Equal(CommonServiceDecisionCodes.CaptureUnavailable.Value, record.CaptureDecisionCode);
    }

    [Fact]
    public void CaptureFaultRetainsIdentityWithoutInventingDecisionOrStrategy()
    {
        var projection = default(ServiceStateProjectionSnapshot);
        var fault = new ServiceFault(
            ServiceFaultCategory.Capture,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(7));
        var terminal = default(BatchReceipt);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            lifecycle: 1,
            configuration: 1,
            strategy: 0,
            cycle: 5,
            default,
            default,
            CommonServiceDecisionCodes.Ready.Value,
            captureDecisionCode: 0,
            true,
            WakePolicy.Immediate,
            false,
            in projection,
            in fault,
            in terminal);

        var record = DecisionJournalRecord.Decision(in observation);

        Assert.Equal((ulong)5, record.FirstCycle);
        Assert.Equal(ServiceFaultCategory.Capture, record.FaultCategory);
        Assert.Equal(0, record.CaptureDecisionCode);
    }

    private static DecisionJournalObservation PublicationObservation(ulong cycleValue, long timestamp) =>
        CreateObservation(
            BatchReceipt.Completed(
                Identity(cycleValue),
                new BatchId(cycleValue),
                actionCount: 1,
                committedCount: 1,
                default,
                new MonotonicTimestamp(timestamp + 1),
                publishedCount: 1),
            timestamp);
}
