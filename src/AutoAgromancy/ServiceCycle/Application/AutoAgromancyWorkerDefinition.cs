using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoAgromancyWorkerDefinition :
    IServiceCycleWorkerDefinition<AutoAgromancyCycleState, AutoAgromancyCycleAction>
{
    public AutoAgromancyCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoAgromancyCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoAgromancyCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoAgromancyCycleState state,
        ServiceActionWriter<AutoAgromancyCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException(
                "Auto Agromancy state belongs to a different lifecycle.");
        var wake = AutoAgromancyCycleEvaluator.Evaluate(
            world, in config, ref state, actions, out var decision);
        state.RecordDecision(in decision);
        return wake;
    }

    public void ProjectState(
        in AutoAgromancyCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoAgromancyServiceProjection.Write(in state, output);
}
