namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

using OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// The worker definition a runner was built around, together with the claim that proves no other
/// live runner shares it.
/// </summary>
internal sealed class ServiceRunnerPreparedResources<TState>
{
    internal ServiceRunnerPreparedResources(
        IServiceCycleWorkerStateDefinition<TState> workerDefinition,
        ServiceResourceClaimLedger claims,
        ServiceResourceClaim workerDefinitionClaim)
    {
        WorkerDefinition = workerDefinition;
        Claims = claims;
        WorkerDefinitionClaim = workerDefinitionClaim;
    }

    internal IServiceCycleWorkerStateDefinition<TState> WorkerDefinition { get; }
    internal ServiceResourceClaimLedger Claims { get; }
    internal ServiceResourceClaim WorkerDefinitionClaim { get; }
}
