using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoItemsWorkerDefinition :
    IServiceCycleWorkerDefinition<AutoItemsCycleState, AutoItemsCycleAction>
{
    public AutoItemsCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoItemsCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoItemsCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoItemsCycleState state,
        ServiceActionWriter<AutoItemsCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Items state belongs to a different lifecycle.");
        state.ObserveConfiguration(context.Identity.Config, config.AutoItems);
        var wake = AutoItemsCycleEvaluator.Evaluate(
            world,
            in config,
            in context,
            ref state,
            actions,
            out var decision);
        state.RecordDecision(in decision);
        return wake;
    }

    public void ProjectState(
        in AutoItemsCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoItemsServiceProjection.Write(in state, output);
}
