using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>The worker for a service that reads the published world.</summary>
internal sealed class ServiceCycleOrdinaryWorker<TState, TAction> :
    ServiceCycleWorker<TState, TAction>
{
    private readonly IServiceCycleWorkerDefinition<TState, TAction> _definition;

    internal ServiceCycleOrdinaryWorker(
        IServiceCycleWorkerDefinition<TState, TAction> definition,
        ServiceId serviceId,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        IMonotonicClock clock,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        bool measureAllocations,
        LifecycleGeneration lifecycle,
        ServiceResourceClaimLedger resourceClaims,
        ServiceResourceClaim workerDefinitionClaim,
        IServiceCycleWorkerExitObserver? exitObserver)
        : base(
            definition,
            serviceId,
            actions,
            handoff,
            clock,
            defaultWakePolicy,
            faultRecoveryPolicy,
            measureAllocations,
            lifecycle,
            resourceClaims,
            workerDefinitionClaim,
            exitObserver) =>
        _definition = definition;

    private protected override WakePolicy EvaluateDefinition(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions) =>
        _definition.Evaluate(in config, world, strategy, in context, ref state, actions);
}

/// <summary>
/// The worker for the service that reads the game.
/// </summary>
/// <remarks>
/// It derives from the buffer the main-thread capture filled and never looks at the published world,
/// which is the one it is about to replace — nor at the strategy, for the same reason it takes no
/// world: what a source shape reads is what it can act on, and a collector's readings are not a
/// matter of policy.
/// </remarks>
internal sealed class ServiceCycleSourceWorker<TState, TAction> :
    ServiceCycleWorker<TState, TAction>
{
    private readonly IServiceCycleSourceWorkerDefinition<TState, TAction> _definition;
    private readonly GameWorldCycleFrame _frame;

    internal ServiceCycleSourceWorker(
        IServiceCycleSourceWorkerDefinition<TState, TAction> definition,
        ServiceId serviceId,
        GameWorldCycleFrame frame,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        IMonotonicClock clock,
        WakePolicy defaultWakePolicy,
        ServiceFaultRecoveryPolicy faultRecoveryPolicy,
        bool measureAllocations,
        LifecycleGeneration lifecycle,
        ServiceResourceClaimLedger resourceClaims,
        ServiceResourceClaim workerDefinitionClaim,
        IServiceCycleWorkerExitObserver? exitObserver)
        : base(
            definition,
            serviceId,
            actions,
            handoff,
            clock,
            defaultWakePolicy,
            faultRecoveryPolicy,
            measureAllocations,
            lifecycle,
            resourceClaims,
            workerDefinitionClaim,
            exitObserver)
    {
        _definition = definition;
        _frame = frame;
    }

    private protected override WakePolicy EvaluateDefinition(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref TState state,
        ServiceActionWriter<TAction> actions) =>
        _definition.Evaluate(_frame, in config, in context, ref state, actions);
}
