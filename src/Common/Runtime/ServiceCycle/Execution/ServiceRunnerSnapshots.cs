using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal enum ServiceRunnerStorageEvidenceAvailability
{
    NotAvailable = 0,
    Exact = 1,
    LastPublished = 2,
    HandoffContended = 3,
}

internal readonly struct ServiceRunnerStorageSnapshot
{
    internal ServiceRunnerStorageSnapshot(
        ServiceRunnerStorageEvidenceAvailability availability,
        int capacity,
        int highWater,
        long growthAllocations,
        int retainedSlots)
    {
        Availability = availability;
        Capacity = capacity;
        HighWater = highWater;
        GrowthAllocations = growthAllocations;
        RetainedSlots = retainedSlots;
    }

    internal ServiceRunnerStorageEvidenceAvailability Availability { get; }
    internal int Capacity { get; }
    internal int HighWater { get; }
    internal long GrowthAllocations { get; }
    internal int RetainedSlots { get; }
}

internal readonly struct ServiceStartDecisionFact
{
    internal ServiceStartDecisionFact(ServiceStartDecision decision, MonotonicTimestamp observedAt)
    {
        Decision = decision;
        ObservedAt = observedAt;
        IsPresent = true;
    }

    internal ServiceStartDecision Decision { get; }
    internal MonotonicTimestamp ObservedAt { get; }
    internal bool IsPresent { get; }
}

internal readonly struct ServiceStartInvocationFact
{
    internal ServiceStartInvocationFact(
        ServiceCycleStartContext context,
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt)
    {
        Context = context;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsPresent = true;
    }

    internal ServiceCycleStartContext Context { get; }
    internal MonotonicTimestamp StartedAt { get; }
    internal MonotonicTimestamp CompletedAt { get; }
    internal bool IsPresent { get; }
}

internal readonly struct ServiceCaptureFact
{
    internal ServiceCaptureFact(
        ServiceCaptureContext context,
        ServiceCaptureResult result,
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt,
        ServiceFault fault = default,
        MonotonicTimestamp retryDue = default)
    {
        Context = context;
        Result = result;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Fault = fault;
        RetryDue = retryDue;
        IsPresent = true;
    }

    internal ServiceCaptureContext Context { get; }
    internal ServiceCaptureResult Result { get; }
    internal MonotonicTimestamp StartedAt { get; }
    internal MonotonicTimestamp CompletedAt { get; }
    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RetryDue { get; }
    internal bool IsPresent { get; }
}

internal readonly struct ServiceFaultRecoveryFact
{
    internal ServiceFaultRecoveryFact(ServiceFault fault, MonotonicTimestamp recoveredAt)
    {
        Fault = fault;
        RecoveredAt = recoveredAt;
    }

    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RecoveredAt { get; }
    internal bool IsPresent => Fault.IsValid;
}

internal readonly struct ServiceActionFact
{
    internal ServiceActionFact(
        ServiceActionContext context,
        ServiceActionResult result,
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt)
    {
        Context = context;
        Result = result;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsPresent = true;
    }

    internal ServiceActionContext Context { get; }
    internal ServiceActionResult Result { get; }
    internal MonotonicTimestamp StartedAt { get; }
    internal MonotonicTimestamp CompletedAt { get; }
    internal bool IsPresent { get; }
}

internal readonly struct ServiceEvaluationTimingFact
{
    internal ServiceEvaluationTimingFact(
        long requestSequence,
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt,
        bool isComplete)
    {
        RequestSequence = requestSequence;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        IsComplete = isComplete;
    }

    internal long RequestSequence { get; }
    internal MonotonicTimestamp StartedAt { get; }
    internal MonotonicTimestamp CompletedAt { get; }
    internal bool IsPresent => RequestSequence > 0;
    internal bool IsComplete { get; }
}

internal enum ServiceRunnerEvaluationTimingAvailability
{
    NotAvailable = 0,
    Available = 1,
    PublicationContended = 2,
}

internal readonly struct ServiceRunnerEvaluationTimingSnapshot
{
    internal ServiceRunnerEvaluationTimingSnapshot(
        ServiceRunnerEvaluationTimingAvailability availability,
        ServiceEvaluationTimingFact fact)
    {
        Availability = availability;
        Fact = fact;
    }

    internal ServiceRunnerEvaluationTimingAvailability Availability { get; }
    internal ServiceEvaluationTimingFact Fact { get; }
}

public readonly struct ServiceProjectionPublication
{
    internal ServiceProjectionPublication(
        ServiceProjectionContext context,
        ServiceStateProjectionSnapshot snapshot,
        ConfigGeneration latestConfiguration)
    {
        Context = context;
        Snapshot = snapshot;
        LatestConfiguration = latestConfiguration;
    }

    public ServiceProjectionContext Context { get; }
    public ServiceStateProjectionSnapshot Snapshot { get; }
    public ConfigGeneration LatestConfiguration { get; }
    public bool IsPresent => Context.Publication.IsValid;
}
