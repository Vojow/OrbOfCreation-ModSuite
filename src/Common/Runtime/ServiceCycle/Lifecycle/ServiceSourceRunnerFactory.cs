using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

/// <summary>
/// Builds the runner for the service that reads the game.
/// </summary>
/// <remarks>
/// The one thing it adds to the ordinary shape is the capture buffer: the runtime constructs it here,
/// one per lifecycle, and hands the same instance to the coordinator that fills it on the main thread
/// and to the worker that derives from it. One per lifecycle rather than one per service, because a
/// retiring worker may still be reading the buffer its own lifecycle captured into while the
/// replacement lifecycle's capture has already begun.
/// </remarks>
internal sealed class ServiceSourceRunnerFactory<TState, TAction> :
    ServiceRunnerFactory<TState, TAction>
{
    private readonly IServiceCycleSourceDefinition<TState, TAction> _definition;

    internal ServiceSourceRunnerFactory(
        IServiceCycleSourceDefinition<TState, TAction> definition,
        in ServiceRunnerFactoryParts parts)
        : base(definition, in parts) =>
        _definition = definition;

    private protected override IServiceCycleWorkerStateDefinition<TState> CreateWorkerDefinition() =>
        _definition.CreateWorkerDefinition();

    private protected override ServiceCycleShapeParts<TState, TAction> CreateCycleParts(
        IServiceCycleWorkerStateDefinition<TState> workerDefinition,
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleMainState main,
        LifecycleGeneration lifecycle,
        ServiceRunnerLifetime lifetime,
        ServiceResourceClaimLedger claims,
        ServiceResourceClaim workerDefinitionClaim)
    {
        var frame = new GameWorldCycleFrame();
        return new ServiceCycleShapeParts<TState, TAction>(
            new ServiceCycleSourceStartCoordinator<TState, TAction>(
                _definition,
                Configuration,
                frame,
                handoff,
                main,
                ServiceIdentity,
                lifecycle,
                FaultRecoveryPolicy,
                Clock,
                lifetime,
                Strategy,
                World),
            new ServiceCycleSourceWorker<TState, TAction>(
                // As on the ordinary path, this narrows what this same factory produced a step
                // earlier; the ledger that carried it between the two admits either shape.
                (IServiceCycleSourceWorkerDefinition<TState, TAction>)workerDefinition,
                ServiceIdentity,
                frame,
                actions,
                handoff,
                Clock,
                DefaultWake,
                FaultRecoveryPolicy,
                MeasureWorkerAllocations,
                lifecycle,
                claims,
                workerDefinitionClaim,
                WorkerExitObserver));
    }
}
