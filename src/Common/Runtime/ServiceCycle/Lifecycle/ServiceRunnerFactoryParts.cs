using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// Everything a runner factory needs besides the definition itself: what the service declared, and
/// the collaborators the registry owns.
/// </summary>
/// <remarks>
/// Bundled so the two registration paths differ only in which factory they name. The declaration
/// values are read from the definition once, by the registry, and carried here — a definition that
/// answered differently on a second read would otherwise register under one identity and run under
/// another.
/// </remarks>
internal readonly struct ServiceRunnerFactoryParts
{
    internal ServiceRunnerFactoryParts(
        ServiceId serviceId,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        ServiceConfigurationPublisher configuration,
        IMonotonicClock clock,
        bool measureWorkerAllocations,
        ServiceResourceClaimLedger resourceClaims,
        IServiceCycleWorkerStarter? workerStarter,
        IServiceCycleWorkerExitObserver? workerExitObserver,
        ServiceStrategyPublisher strategy,
        ServiceWorldPublisher<GameWorldState> world)
    {
        ServiceId = serviceId;
        DefaultWakePolicy = defaultWakePolicy;
        FaultRecoveryPolicy = faultRecoveryPolicy;
        Configuration = configuration;
        Clock = clock;
        MeasureWorkerAllocations = measureWorkerAllocations;
        ResourceClaims = resourceClaims;
        WorkerStarter = workerStarter;
        WorkerExitObserver = workerExitObserver;
        Strategy = strategy;
        World = world;
    }

    internal ServiceId ServiceId { get; }
    internal WakePolicy DefaultWakePolicy { get; }
    internal ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; }
    internal ServiceConfigurationPublisher Configuration { get; }
    internal IMonotonicClock Clock { get; }
    internal bool MeasureWorkerAllocations { get; }
    internal ServiceResourceClaimLedger ResourceClaims { get; }
    internal IServiceCycleWorkerStarter? WorkerStarter { get; }
    internal IServiceCycleWorkerExitObserver? WorkerExitObserver { get; }
    internal ServiceStrategyPublisher Strategy { get; }
    internal ServiceWorldPublisher<GameWorldState> World { get; }
}
