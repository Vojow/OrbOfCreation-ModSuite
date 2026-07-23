using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceCycleMainState<TConfig>
    where TConfig : notnull
{
    internal ConfigurationPublication<TConfig>? CycleConfiguration;
    internal ServiceCycleIdentity InFlightCycle;
    internal BatchId InFlightBatch;
    internal bool HasInFlightCycle;
    internal ServiceCycleIdentity ActiveCycle;
    internal BatchId ActiveBatch;
    internal WakePolicy ActiveWake;
    internal MonotonicTimestamp ResponsePublishedAt;
    internal MonotonicTimestamp NextWakeDue;
    internal bool HasWakeDue;
    internal BatchReceipt PreviousReceipt;
    internal ServiceProjectionPublication Projection;
    internal ServiceFault LatestFault;
    internal ServiceNativeCallTotals NativeOutcome;
    internal int CommittedCount;
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
}
