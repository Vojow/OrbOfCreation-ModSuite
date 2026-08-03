using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.World;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalObserverTestData
{
    /// <summary>
    /// An observer that has already hit the pending-cycle guard, carrying the fault it kept.
    /// </summary>
    internal static ServiceCycleDecisionJournalObserver FaultedOnMismatchedResponse()
    {
        var journal = new DecisionJournalCoalescer(
            1,
            new DiscardingSink(),
            new MonotonicDuration(100),
            default);
        var observer = new ServiceCycleDecisionJournalObserver(journal, 1);
        observer.BindPublications(new ConfigGeneration(1), new StrategyGeneration(1));
        observer.Bind(
            0,
            new LifecycleGeneration(1),
            default,
            lifecycleSemanticVersion: 1,
            lifecycleTerminalSequence: 0,
            constructionDeferralSequence: 0,
            worldGateDeferralSequence: 0);
        var start = CapturedAttempt(1);
        var mismatched = SuccessfulResponse(2, actionCount: 0);
        observer.StartAttemptObserved(0, in start, new MonotonicTimestamp(20));
        observer.ResponseAcquired(0, in mismatched, new MonotonicTimestamp(32));
        return observer;
    }

    internal static ServiceCycleStartAttempt CapturedAttempt(
        ulong cycleValue,
        bool queued = true,
        ServiceFaultRecoveryFact recovery = default,
        ulong lifecycleValue = 1,
        ulong strategyValue = 1)
    {
        var cycle = Identity(cycleValue, lifecycleValue: lifecycleValue, strategyValue: strategyValue);
        var startContext = new ServiceCycleStartContext(
            cycle.Lifecycle,
            cycle.Config,
            default,
            new MonotonicTimestamp(10));
        var start = ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        var startFact = new ServiceStartDecisionFact(start, new MonotonicTimestamp(11));
        var captureContext = new ServiceCaptureContext(
            cycle.Service,
            cycle.Lifecycle,
            cycle.Config,
            cycle.Strategy,
            new CaptureSequence(cycle.Cycle.Value),
            cycle.Cycle,
            GameWorldStateDefaults.Empty,
            new MonotonicTimestamp(12));
        var capture = ServiceCaptureResult.Captured(CommonServiceDecisionCodes.Captured);
        var captureFact = new ServiceCaptureFact(
            captureContext,
            capture,
            new MonotonicTimestamp(12),
            new MonotonicTimestamp(13));
        return new ServiceCycleStartAttempt(
            queued,
            startFact,
            captureFact,
            cycle,
            new BatchId(cycleValue),
            queued ? new MonotonicTimestamp(14) : default,
            recoveredFault: recovery,
            startInvocation: new ServiceStartInvocationFact(
                startContext,
                new MonotonicTimestamp(10),
                new MonotonicTimestamp(11)));
    }

    internal static ServiceCycleStartAttempt DeferredPublication(ulong cycleValue) => new(
        true,
        new ServiceStartDecisionFact(
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready),
            new MonotonicTimestamp(11)),
        default,
        Identity(cycleValue),
        new BatchId(cycleValue),
        new MonotonicTimestamp(15));

    internal static ServiceCycleStartAttempt CaptureUnavailable(
        ulong cycleValue,
        ulong strategyValue = 1)
    {
        var cycle = Identity(cycleValue, strategyValue: strategyValue);
        var startContext = new ServiceCycleStartContext(
            cycle.Lifecycle,
            cycle.Config,
            default,
            new MonotonicTimestamp(20));
        var start = ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        var captureContext = new ServiceCaptureContext(
            cycle.Service,
            cycle.Lifecycle,
            cycle.Config,
            cycle.Strategy,
            new CaptureSequence(cycle.Cycle.Value),
            cycle.Cycle,
            GameWorldStateDefaults.Empty,
            new MonotonicTimestamp(21));
        var capture = ServiceCaptureResult.Unavailable(
            CommonServiceDecisionCodes.CaptureUnavailable,
            WakePolicy.AfterDecision(new MonotonicDuration(9)));
        return new ServiceCycleStartAttempt(
            false,
            new ServiceStartDecisionFact(start, new MonotonicTimestamp(21)),
            new ServiceCaptureFact(
                captureContext,
                capture,
                new MonotonicTimestamp(21),
                new MonotonicTimestamp(22)),
            default,
            new BatchId(cycleValue),
            default,
            startInvocation: new ServiceStartInvocationFact(
                startContext,
                new MonotonicTimestamp(20),
                new MonotonicTimestamp(21)));
    }

    internal static ServiceResponseAcquisition SuccessfulResponse(
        ulong cycleValue,
        int actionCount,
        ServiceFaultRecoveryFact recovery = default,
        ulong lifecycleValue = 1,
        ulong strategyValue = 1)
    {
        var cycle = Identity(cycleValue, lifecycleValue: lifecycleValue, strategyValue: strategyValue);
        var projection = Projection(70 + checked((int)cycleValue));
        var terminal = actionCount == 0
            ? BatchReceipt.Completed(
                cycle,
                new BatchId(cycleValue),
                0,
                default,
                new MonotonicTimestamp(31))
            : default;
        var response = ServiceWorkerResponse.Success(
            sequence: checked((long)cycleValue),
            cycle,
            new BatchId(cycleValue),
            new MonotonicTimestamp(25),
            new MonotonicTimestamp(29),
            WakePolicy.AfterBatch(new MonotonicDuration(5)),
            new MonotonicTimestamp(30),
            new MonotonicTimestamp(35),
            new ServiceProjectionContext(
                cycle,
                new StatePublicationId(cycleValue),
                new MonotonicTimestamp(29)),
            in projection,
            new ServiceActionStoreMetrics(actionCount, 0, actionCount, actionCount, 0),
            actionCount,
            terminal,
            recovery);
        return new ServiceResponseAcquisition(in response, terminal);
    }

    internal static ServiceResponseAcquisition FailedResponse(
        ulong cycleValue,
        ServiceFault fault,
        ulong lifecycleValue = 1)
    {
        var response = ServiceWorkerResponse.Failure(
            sequence: checked((long)cycleValue),
            Identity(cycleValue, lifecycleValue: lifecycleValue),
            new BatchId(cycleValue),
            new MonotonicTimestamp(25),
            new MonotonicTimestamp(29),
            fault,
            new MonotonicTimestamp(40),
            default);
        return new ServiceResponseAcquisition(in response, default);
    }

    internal static ServiceActionDispatch CompletedAction(ulong cycleValue)
    {
        var cycle = Identity(cycleValue);
        var completedAt = new MonotonicTimestamp(45);
        var native = new ServiceNativeCallTotals(1, 1, 1);
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var result = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        var context = new ServiceActionContext(
            cycle,
            new BatchId(cycleValue),
            new ActionId(1),
            0,
            new MonotonicTimestamp(44));
        var fact = new ServiceActionFact(context, result, new MonotonicTimestamp(44), completedAt);
        var receipt = BatchReceipt.Completed(
            cycle,
            new BatchId(cycleValue),
            1,
            native,
            completedAt);
        var attribution = ServiceActionJournalAttribution.Native(
            new Guid("11111111-1111-1111-1111-111111111111"),
            ServiceActionNativeTypeId.StructureSO);
        return new ServiceActionDispatch(fact, attribution, true, receipt);
    }

    internal static ServiceActionDispatch FaultedAction(ulong cycleValue)
    {
        var cycle = Identity(cycleValue);
        var completedAt = new MonotonicTimestamp(45);
        var result = ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        var context = new ServiceActionContext(
            cycle,
            new BatchId(cycleValue),
            new ActionId(1),
            0,
            new MonotonicTimestamp(44));
        var fact = new ServiceActionFact(context, result, new MonotonicTimestamp(44), completedAt);
        var receipt = BatchReceipt.Terminated(
            cycle,
            new BatchId(cycleValue),
            actionCount: 1,
            committedCount: 0,
            terminalIndex: 0,
            result,
            default,
            completedAt);
        var fault = new ServiceFault(
            ServiceFaultCategory.ActionExecution,
            CommonActionResultCodes.AdapterFault,
            1,
            completedAt);
        var attribution = ServiceActionJournalAttribution.Native(
            new Guid("11111111-1111-1111-1111-111111111111"),
            ServiceActionNativeTypeId.StructureSO);
        return new ServiceActionDispatch(fact, attribution, true, receipt, fault);
    }

    /// <summary>
    /// The world collector's shape: one action that committed by publishing a snapshot, so the batch
    /// truthfully reports no native call at all.
    /// </summary>
    internal static ServiceActionDispatch PublishedAction(ulong cycleValue)
    {
        var cycle = Identity(cycleValue);
        var completedAt = new MonotonicTimestamp(45);
        var result = ServiceActionResult.CommittedPublication(
            CommonActionResultCodes.Committed,
            ServicePublicationEvidence.World(new WorldGeneration(4096)));
        var context = new ServiceActionContext(
            cycle,
            new BatchId(cycleValue),
            new ActionId(1),
            0,
            new MonotonicTimestamp(44));
        var fact = new ServiceActionFact(context, result, new MonotonicTimestamp(44), completedAt);
        var receipt = BatchReceipt.Completed(
            cycle,
            new BatchId(cycleValue),
            actionCount: 1,
            committedCount: 1,
            default,
            completedAt);
        return new ServiceActionDispatch(
            fact,
            ServiceActionJournalAttribution.Publication,
            true,
            receipt);
    }

    private sealed class DiscardingSink : IDecisionJournalRecordSink
    {
        public bool TryAppend(in DecisionJournalRecord record) => true;
        public bool TryFlush() => true;
        public void Stop() { }
    }
}
