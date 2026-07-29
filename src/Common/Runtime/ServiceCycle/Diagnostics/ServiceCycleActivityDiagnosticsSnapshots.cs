using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public readonly struct ServiceCycleStartDecisionDiagnosticsFact
{
    internal ServiceCycleStartDecisionDiagnosticsFact(
        ServiceStartDecision decision,
        MonotonicTimestamp observedAt,
        bool isPresent)
    {
        Decision = decision;
        ObservedAt = observedAt;
        IsPresent = isPresent;
    }

    public ServiceStartDecision Decision { get; }
    public MonotonicTimestamp ObservedAt { get; }
    public bool IsPresent { get; }
}

public readonly struct ServiceCycleCaptureDiagnosticsFact
{
    internal ServiceCycleCaptureDiagnosticsFact(
        ServiceCaptureResult result,
        MonotonicTimestamp startedAt,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration,
        bool isPresent)
    {
        Result = result;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Duration = duration;
        IsPresent = isPresent;
    }

    public ServiceCaptureResult Result { get; }
    public MonotonicTimestamp StartedAt { get; }
    public MonotonicTimestamp CompletedAt { get; }
    public MonotonicDuration Duration { get; }
    public bool IsPresent { get; }
}

public readonly struct ServiceCycleActionDiagnosticsFact
{
    internal ServiceCycleActionDiagnosticsFact(
        ServiceActionContext context,
        ServiceActionResult result,
        MonotonicTimestamp completedAt,
        MonotonicDuration duration,
        bool isPresent)
    {
        Context = context;
        Result = result;
        CompletedAt = completedAt;
        Duration = duration;
        IsPresent = isPresent;
    }

    public ServiceActionContext Context { get; }
    public ServiceActionResult Result { get; }
    public MonotonicTimestamp CompletedAt { get; }
    public MonotonicDuration Duration { get; }
    public bool IsPresent { get; }
}

public readonly struct ServiceCycleTimingDiagnosticsSnapshot
{
    internal ServiceCycleTimingDiagnosticsSnapshot(
        MonotonicTimestamp observedAt,
        MonotonicTimestamp evaluationStartedAt,
        MonotonicTimestamp evaluationCompletedAt,
        MonotonicDuration evaluationDuration,
        MonotonicDuration evaluationAge,
        ServiceCycleEvaluationTimingAvailability evaluationAvailability,
        bool evaluationComplete,
        MonotonicDuration responseAge,
        bool hasResponseAge,
        MonotonicDuration wakeLateness,
        bool wakeIsLate)
    {
        ObservedAt = observedAt;
        EvaluationStartedAt = evaluationStartedAt;
        EvaluationCompletedAt = evaluationCompletedAt;
        EvaluationDuration = evaluationDuration;
        EvaluationAge = evaluationAge;
        EvaluationAvailability = evaluationAvailability;
        EvaluationComplete = evaluationComplete;
        ResponseAge = responseAge;
        HasResponseAge = hasResponseAge;
        WakeLateness = wakeLateness;
        WakeIsLate = wakeIsLate;
    }

    public MonotonicTimestamp ObservedAt { get; }
    public MonotonicTimestamp EvaluationStartedAt { get; }
    public MonotonicTimestamp EvaluationCompletedAt { get; }
    public MonotonicDuration EvaluationDuration { get; }
    public MonotonicDuration EvaluationAge { get; }
    public ServiceCycleEvaluationTimingAvailability EvaluationAvailability { get; }
    public bool HasEvaluation =>
        EvaluationAvailability == ServiceCycleEvaluationTimingAvailability.Available;
    public bool EvaluationComplete { get; }
    public MonotonicDuration ResponseAge { get; }
    public bool HasResponseAge { get; }
    public MonotonicDuration WakeLateness { get; }
    public bool WakeIsLate { get; }
}

public readonly struct ServiceCycleBatchDiagnosticsSnapshot
{
    internal ServiceCycleBatchDiagnosticsSnapshot(
        ServiceCycleIdentity cycle,
        BatchId batch,
        WakePolicy wakePolicy,
        MonotonicTimestamp responsePublishedAt,
        MonotonicDuration age,
        int actionCount,
        int actionCursor,
        int actionCapacity,
        int actionHighWater,
        long actionGrowthAllocations,
        int retainedActionSlots,
        int committedCount,
        ServiceNativeCallTotals nativeOutcome,
        bool isPresent)
    {
        Cycle = cycle;
        Batch = batch;
        WakePolicy = wakePolicy;
        ResponsePublishedAt = responsePublishedAt;
        Age = age;
        ActionCount = actionCount;
        ActionCursor = actionCursor;
        ActionCapacity = actionCapacity;
        ActionHighWater = actionHighWater;
        ActionGrowthAllocations = actionGrowthAllocations;
        RetainedActionSlots = retainedActionSlots;
        CommittedCount = committedCount;
        NativeOutcome = nativeOutcome;
        IsPresent = isPresent;
    }

    public ServiceCycleIdentity Cycle { get; }
    public BatchId Batch { get; }
    public WakePolicy WakePolicy { get; }
    public MonotonicTimestamp ResponsePublishedAt { get; }
    public MonotonicDuration Age { get; }
    public int ActionCount { get; }
    public int ActionCursor { get; }
    public int ActionCapacity { get; }
    public int ActionHighWater { get; }
    public long ActionGrowthAllocations { get; }
    public int RetainedActionSlots { get; }
    public int CommittedCount { get; }
    public ServiceNativeCallTotals NativeOutcome { get; }
    public bool IsPresent { get; }
}

public readonly struct ServiceCycleHandoffDiagnosticsSnapshot
{
    internal ServiceCycleHandoffDiagnosticsSnapshot(
        ServiceCycleHandoffDiagnosticsPhase phase,
        long requestSequence,
        long transitionCount,
        long workerWaitCount,
        long cleanupRequestCount,
        long cleanupAcknowledgementCount,
        int lastCleanupThreadId,
        bool cleanupPending,
        bool stopRequested)
    {
        Phase = phase;
        RequestSequence = requestSequence;
        TransitionCount = transitionCount;
        WorkerWaitCount = workerWaitCount;
        CleanupRequestCount = cleanupRequestCount;
        CleanupAcknowledgementCount = cleanupAcknowledgementCount;
        LastCleanupThreadId = lastCleanupThreadId;
        CleanupPending = cleanupPending;
        StopRequested = stopRequested;
    }

    public ServiceCycleHandoffDiagnosticsPhase Phase { get; }
    public long RequestSequence { get; }
    public long TransitionCount { get; }
    public long WorkerWaitCount { get; }
    public long CleanupRequestCount { get; }
    public long CleanupAcknowledgementCount { get; }
    public int LastCleanupThreadId { get; }
    public bool CleanupPending { get; }
    public bool StopRequested { get; }
}

public readonly struct ServiceCycleWorkerDiagnosticsSnapshot
{
    internal ServiceCycleWorkerDiagnosticsSnapshot(
        int threadId,
        bool isBackground,
        long lastCycleAllocatedBytes,
        long measuredCycleCount,
        long stateFactoryContentionCount)
    {
        ThreadId = threadId;
        IsBackground = isBackground;
        LastCycleAllocatedBytes = lastCycleAllocatedBytes;
        MeasuredCycleCount = measuredCycleCount;
        StateFactoryContentionCount = stateFactoryContentionCount;
    }

    public int ThreadId { get; }
    public bool IsBackground { get; }
    public long LastCycleAllocatedBytes { get; }
    public long MeasuredCycleCount { get; }
    public long StateFactoryContentionCount { get; }
}
