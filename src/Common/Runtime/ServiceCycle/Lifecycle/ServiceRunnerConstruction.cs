using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal readonly struct ServiceRunnerConstructionResult<TState, TAction>
{
    internal ServiceRunnerConstructionResult(
        ServiceRunner<TState, TAction>? runner,
        bool contended)
    {
        Runner = runner;
        Contended = contended;
    }

    internal ServiceRunner<TState, TAction>? Runner { get; }
    internal bool Contended { get; }
}

internal readonly struct ServiceRunnerParts<TState, TAction>
{
    internal ServiceRunnerParts(
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff handoff,
        ServiceCycleWorker<TState, TAction> worker,
        ServiceCycleMainState main,
        ServiceCycleStartCoordinator<TState, TAction> starts,
        ServiceBatchResponseHandler<TState, TAction> responses,
        ServiceBatchActionExecutor<TState, TAction> actionExecutor,
        ServiceBatchCompletion<TState, TAction> batchCompletion,
        ServiceRunnerDiagnosticsAssembler<TState, TAction> diagnostics,
        ServiceRunnerLifetime lifetime,
        ServiceRunnerResourceIdentity resourceIdentity)
    {
        Actions = actions;
        Handoff = handoff;
        Worker = worker;
        Main = main;
        Starts = starts;
        Responses = responses;
        ActionExecutor = actionExecutor;
        BatchCompletion = batchCompletion;
        Diagnostics = diagnostics;
        Lifetime = lifetime;
        ResourceIdentity = resourceIdentity;
    }

    internal ReusableActionStore<TAction> Actions { get; }
    internal ServiceCycleHandoff Handoff { get; }
    internal ServiceCycleWorker<TState, TAction> Worker { get; }
    internal ServiceCycleMainState Main { get; }
    internal ServiceCycleStartCoordinator<TState, TAction> Starts { get; }
    internal ServiceBatchResponseHandler<TState, TAction> Responses { get; }
    internal ServiceBatchActionExecutor<TState, TAction> ActionExecutor { get; }
    internal ServiceBatchCompletion<TState, TAction> BatchCompletion { get; }
    internal ServiceRunnerDiagnosticsAssembler<TState, TAction> Diagnostics { get; }
    internal ServiceRunnerLifetime Lifetime { get; }
    internal ServiceRunnerResourceIdentity ResourceIdentity { get; }
}
