using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal readonly struct ServiceRunnerConstructionResult<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceRunnerConstructionResult(
        ServiceRunner<TFrame, TConfig, TState, TAction>? runner,
        bool contended)
    {
        Runner = runner;
        Contended = contended;
    }

    internal ServiceRunner<TFrame, TConfig, TState, TAction>? Runner { get; }
    internal bool Contended { get; }
}

internal readonly struct ServiceRunnerParts<TFrame, TConfig, TState, TAction>
    where TConfig : notnull
{
    internal ServiceRunnerParts(
        ReusableActionStore<TAction> actions,
        ServiceCycleHandoff<TConfig> handoff,
        ServiceCycleWorker<TFrame, TConfig, TState, TAction> worker,
        ServiceCycleMainState<TConfig> main,
        ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction> starts,
        ServiceBatchResponseHandler<TFrame, TConfig, TState, TAction> responses,
        ServiceBatchActionExecutor<TFrame, TConfig, TState, TAction> actionExecutor,
        ServiceBatchCompletion<TFrame, TConfig, TState, TAction> batchCompletion,
        ServiceRunnerDiagnosticsAssembler<TFrame, TConfig, TState, TAction> diagnostics,
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
    internal ServiceCycleHandoff<TConfig> Handoff { get; }
    internal ServiceCycleWorker<TFrame, TConfig, TState, TAction> Worker { get; }
    internal ServiceCycleMainState<TConfig> Main { get; }
    internal ServiceCycleStartCoordinator<TFrame, TConfig, TState, TAction> Starts { get; }
    internal ServiceBatchResponseHandler<TFrame, TConfig, TState, TAction> Responses { get; }
    internal ServiceBatchActionExecutor<TFrame, TConfig, TState, TAction> ActionExecutor { get; }
    internal ServiceBatchCompletion<TFrame, TConfig, TState, TAction> BatchCompletion { get; }
    internal ServiceRunnerDiagnosticsAssembler<TFrame, TConfig, TState, TAction> Diagnostics { get; }
    internal ServiceRunnerLifetime Lifetime { get; }
    internal ServiceRunnerResourceIdentity ResourceIdentity { get; }
}
