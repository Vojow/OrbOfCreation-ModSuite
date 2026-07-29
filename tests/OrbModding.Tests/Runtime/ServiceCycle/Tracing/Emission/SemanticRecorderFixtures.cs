using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing.Emission;

internal static class SemanticRecorderFixtures
{
    internal static readonly ServiceCycleIdentity Cycle = new(
        new ServiceId("test.service"),
        new LifecycleGeneration(2),
        new ConfigGeneration(3),
        new StrategyGeneration(4),
        new WorldGeneration(1),
        new CycleId(6));

    internal static readonly ServiceFault Fault = new(
        ServiceFaultCategory.Evaluation,
        CommonActionResultCodes.AdapterFault,
        2,
        new MonotonicTimestamp(90));

    internal static readonly ServiceCaptureContext Capture = new(
        Cycle.Service,
        Cycle.Lifecycle,
        Cycle.Config,
        Cycle.Strategy,
        new CaptureSequence(5),
        Cycle.Cycle,
        GameWorldStateDefaults.Empty,
        new MonotonicTimestamp(10));

    internal static ServiceActionContext ActionContext(int index = 0) => new(
        Cycle,
        new BatchId(8),
        new ActionId((ulong)index + 10),
        index,
        new MonotonicTimestamp(100));

    internal static ServiceActionResult CommittedAction() => ServiceActionResult.Committed(
        CommonActionResultCodes.Committed,
        ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1)));

    internal static ServiceProjectionPublication ProjectionPublication()
    {
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(new ServiceProjectionKey(7), ServiceProjectionValue.FromBoolean(true));
        builder.Add(new ServiceProjectionKey(9), ServiceProjectionValue.FromInteger(-42));
        builder.Add(new ServiceProjectionKey(11), ServiceProjectionValue.FromFloatingPoint(2.5));
        return new ServiceProjectionPublication(
            new ServiceProjectionContext(Cycle, new StatePublicationId(12), new MonotonicTimestamp(110)),
            buffer.CreateSnapshot(),
            new ConfigGeneration(3));
    }

    internal static SuiteFramePumpReport Pump(
        bool accepted,
        int actions = 0,
        int captures = 0,
        int responses = 0) => new(
            frameIdentity: 20,
            accepted,
            startingOrdinal: 1,
            responsesAcquired: responses,
            actionsAttempted: actions,
            capturesAttempted: captures,
            cyclesStarted: captures,
            worldGateDeferrals: 0,
            emergencyBatchesRejected: 0,
            lifecyclePositionTransitions: 0,
            responseDuration: new MonotonicDuration(2),
            actionDuration: new MonotonicDuration(3),
            captureDuration: new MonotonicDuration(4),
            totalDuration: new MonotonicDuration(10));
}
