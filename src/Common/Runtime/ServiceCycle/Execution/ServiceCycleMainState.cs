using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceCycleMainState
{
    internal ConfigurationPublication? CycleConfiguration;
    internal ServiceCycleIdentity InFlightCycle;
    internal BatchId InFlightBatch;
    internal bool HasInFlightCycle;
    internal ServiceCycleIdentity ActiveCycle;
    internal BatchId ActiveBatch;
    internal WakePolicy ActiveWake;
    internal MonotonicTimestamp ResponsePublishedAt;
    internal MonotonicTimestamp NextWakeDue;
    internal bool HasWakeDue;
    internal ConfigGeneration WakeConfigurationGeneration;
    internal bool WakeInvalidatedByConfiguration;
    internal BatchReceipt PreviousReceipt;
    internal ServiceProjectionPublication Projection;
    internal ServiceFault LatestFault;
    internal ServiceNativeCallTotals NativeOutcome;
    internal int CommittedCount;

    /// <summary>Committed actions in this batch that published rather than mutated the game.</summary>
    internal int PublishedCount;
    internal int PreNativeSkippedCount;
    internal ConfigGeneration LatestConfigGeneration;
    internal int ActionCount;
    internal int ActionCursor;
    internal int ActionCapacity;
    internal int ActionHighWater;
    internal long ActionGrowthAllocations;
    internal int RetainedActionSlots;
    internal bool HasActiveBatch;
    internal ServiceStartDecisionFact LastStartDecision;
    internal ServiceCaptureFact LastCapture;
    internal ServiceActionFact LastAction;
    internal ServiceEvaluationTimingFact EvaluationTiming;

    internal void ScheduleWake(
        MonotonicTimestamp due,
        ConfigGeneration configurationGeneration,
        bool invalidatedByConfiguration = true)
    {
        if (!configurationGeneration.IsValid)
            throw new System.ArgumentException(
                "A valid configuration generation is required.",
                nameof(configurationGeneration));
        NextWakeDue = due;
        WakeConfigurationGeneration = configurationGeneration;
        WakeInvalidatedByConfiguration = invalidatedByConfiguration;
        HasWakeDue = true;
    }

    internal void ClearWake()
    {
        HasWakeDue = false;
        WakeConfigurationGeneration = default;
        WakeInvalidatedByConfiguration = false;
    }
}
