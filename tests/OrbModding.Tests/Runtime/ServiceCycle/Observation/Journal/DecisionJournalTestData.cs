using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

internal static class DecisionJournalTestData
{
    internal static DecisionJournalObservation CreateObservation(
        ulong cycleValue,
        long timestamp,
        int projectionValue = 7,
        long wakeTicks = 5,
        int faultOccurrence = 0,
        ulong serviceValue = 1)
    {
        var cycle = Identity(cycleValue, serviceValue);
        var terminal = BatchReceipt.Completed(
            cycle,
            new BatchId(cycleValue),
            1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(timestamp + 1));
        var fault = faultOccurrence == 0
            ? default
            : new ServiceFault(
                ServiceFaultCategory.ActionExecution,
                CommonActionResultCodes.AdapterFault,
                faultOccurrence,
                new MonotonicTimestamp(timestamp));
        var projection = Projection(projectionValue);
        return new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(serviceValue),
            1,
            1,
            1,
            cycleValue,
            cycleValue,
            new MonotonicTimestamp(timestamp),
            terminal.CompletedAt,
            CommonServiceDecisionCodes.Ready.Value,
            CommonServiceDecisionCodes.Captured.Value,
            true,
            WakePolicy.AfterBatch(new MonotonicDuration(wakeTicks)),
            true,
            in projection,
            in fault,
            in terminal);
    }

    internal static DecisionJournalObservation CreateObservation(
        in BatchReceipt terminal,
        long timestamp,
        ulong serviceValue = 1)
    {
        var cycle = terminal.Cycle;
        var projection = Projection(7);
        var fault = default(ServiceFault);
        return new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(serviceValue),
            cycle.Lifecycle.Value,
            cycle.Config.Value,
            cycle.Strategy.Value,
            cycle.Capture.Value,
            cycle.Cycle.Value,
            new MonotonicTimestamp(timestamp),
            terminal.CompletedAt,
            CommonServiceDecisionCodes.Ready.Value,
            CommonServiceDecisionCodes.Captured.Value,
            true,
            WakePolicy.AfterBatch(new MonotonicDuration(5)),
            true,
            in projection,
            in fault,
            in terminal);
    }

    internal static ServiceCycleIdentity Identity(
        ulong cycleValue,
        ulong serviceValue = 1,
        ulong lifecycleValue = 1) =>
        new(
            new ServiceId("test.service." + serviceValue),
            new LifecycleGeneration(lifecycleValue),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new CaptureSequence(cycleValue),
            new CycleId(cycleValue));

    internal static ServiceStateProjectionSnapshot Projection(long value)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromInteger(value));
        return builder.CaptureSnapshot();
    }
}
