using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal enum PendingMainOwnershipCompletion
{
    None = 0,
    ReturnEmpty = 1,
    WorkerCleanup = 2,
}

internal sealed class ServiceBatchRuntime<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceBatchRuntime(
        IServiceCycleDefinition<TFrame, TConfig, TState, TAction> definition,
        ServiceConfigurationPublisher<TConfig> configuration,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff<TConfig> handoff,
        ServiceCycleMainState<TConfig> state,
        ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction> starts,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        IMonotonicClock clock,
        LifecycleGeneration lifecycle,
        ServiceRunnerLifetime lifetime)
    {
        Definition = definition;
        Configuration = configuration;
        Actions = actions;
        Handoff = handoff;
        State = state;
        Starts = starts;
        ActionFaults = new ServiceFaultTracker(faultRecoveryPolicy);
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Lifecycle = lifecycle;
        Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        actions.ValidateLifecycle(lifecycle);
    }

    internal IServiceCycleDefinition<TFrame, TConfig, TState, TAction> Definition { get; }
    internal ServiceConfigurationPublisher<TConfig> Configuration { get; }
    internal ReusableActionStore<TAction> Actions { get; }
    internal ServiceCycleHandoff<TConfig> Handoff { get; }
    internal ServiceCycleMainState<TConfig> State { get; }
    internal ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction> Starts { get; }
    internal ServiceFaultTracker ActionFaults { get; }
    internal IMonotonicClock Clock { get; }
    internal LifecycleGeneration Lifecycle { get; }
    internal ServiceRunnerLifetime Lifetime { get; }
    internal PendingMainOwnershipCompletion PendingCompletion { get; set; }
    internal int PendingCleanupFrom { get; set; }
    internal int PendingCleanupCount { get; set; }
    internal EmergencyStopContext OutstandingResponseEmergency { get; set; }

    internal void PublishActionMetrics(ServiceActionStoreMetrics metrics)
    {
        State.ActionCount = metrics.Count;
        State.ActionCursor = metrics.Cursor;
        State.ActionCapacity = metrics.Capacity;
        State.ActionHighWater = metrics.HighWaterCount;
        State.ActionGrowthAllocations = metrics.GrowthAllocationCount;
        State.RetainedActionSlots = metrics.RetainedSlots;
    }

    internal void ClearVisibleActionBatch()
    {
        State.ActionCount = 0;
        State.ActionCursor = 0;
    }
}
