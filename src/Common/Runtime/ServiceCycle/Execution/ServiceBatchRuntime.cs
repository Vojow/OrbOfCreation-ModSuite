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

internal sealed class ServiceBatchRuntime<TState, TAction>
{
    internal ServiceBatchRuntime(
        IServiceCycleMainThreadDefinition<TAction> definition,
        ServiceConfigurationPublisher configuration,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState state,
        ServiceCycleStartCoordinator<TState, TAction> starts,
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

    internal IServiceCycleMainThreadDefinition<TAction> Definition { get; }
    internal ServiceConfigurationPublisher Configuration { get; }
    internal ReusableActionStore<TAction> Actions { get; }
    internal ServiceCycleHandoff Handoff { get; }
    internal ServiceCycleMainState State { get; }
    internal ServiceCycleStartCoordinator<TState, TAction> Starts { get; }
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
