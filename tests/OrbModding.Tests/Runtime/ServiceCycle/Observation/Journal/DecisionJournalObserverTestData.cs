using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalObserverTestData
{
    internal static ServiceCycleStartAttempt CapturedAttempt(
        ulong cycleValue,
        bool queued = true,
        ServiceFaultRecoveryFact recovery = default,
        ulong lifecycleValue = 1)
    {
        var cycle = Identity(cycleValue, lifecycleValue: lifecycleValue);
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
            cycle.Capture,
            cycle.Cycle,
            new MonotonicTimestamp(12));
        var capture = ServiceCaptureResult.Captured(
            cycle.Strategy,
            CommonServiceDecisionCodes.Captured);
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

    internal static ServiceCycleStartAttempt CaptureUnavailable(ulong cycleValue)
    {
        var cycle = Identity(cycleValue);
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
            cycle.Capture,
            cycle.Cycle,
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
        ulong lifecycleValue = 1)
    {
        var cycle = Identity(cycleValue, lifecycleValue: lifecycleValue);
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
        return new ServiceActionDispatch(fact, true, receipt);
    }
}
