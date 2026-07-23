using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;

internal static class ServiceCycleReplayContextFactory
{
    internal static ServiceCycleContext Create(
        ServiceId service,
        in ServiceCycleReplayContext recorded)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (!recorded.Cycle.IsValid)
            throw new ArgumentException("A valid replay context is required.", nameof(recorded));
        var cycle = recorded.Cycle;
        var previousReceipt = recorded.PreviousReceipt;
        var identity = Identity(service, in cycle);
        var receipt = Receipt(service, in previousReceipt);
        return new ServiceCycleContext(identity, receipt, new MonotonicTimestamp(recorded.DecisionAt));
    }

    internal static ServiceCycleIdentity Identity(
        ServiceId service,
        in ServiceCycleReplayCycleKey key) => new(
        service,
        new LifecycleGeneration(key.Lifecycle),
        new ConfigGeneration(key.Configuration),
        new StrategyGeneration(key.Strategy),
        new CaptureSequence(key.Capture),
        new CycleId(key.Cycle));

    internal static BatchReceipt Receipt(
        ServiceId service,
        in ServiceCycleReplayReceipt recorded)
    {
        if (!recorded.IsPresent) return default;
        var recordedCycle = recorded.Cycle;
        var cycle = Identity(service, in recordedCycle);
        var batch = new BatchId(recorded.Batch);
        var native = recorded.NativeCallOutcome;
        var completedAt = new MonotonicTimestamp(recorded.CompletedAt);
        return recorded.Disposition switch
        {
            BatchTerminalDisposition.Completed => BatchReceipt.Completed(
                cycle,
                batch,
                recorded.ActionCount,
                native,
                completedAt),
            BatchTerminalDisposition.Orphaned => BatchReceipt.Orphaned(
                cycle,
                batch,
                recorded.ActionCount,
                recorded.CommittedCount,
                native,
                completedAt),
            BatchTerminalDisposition.Rejected or BatchTerminalDisposition.Faulted => BatchReceipt.Terminated(
                cycle,
                batch,
                recorded.ActionCount,
                recorded.CommittedCount,
                recorded.TerminalIndex,
                recorded.TerminalAction,
                native,
                completedAt,
                recorded.EmergencyStop),
            _ => throw new InvalidOperationException("The replay receipt disposition is invalid."),
        };
    }
}
