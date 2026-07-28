using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal sealed class AutoConceptWorkerDefinition :
    IServiceCycleWorkerDefinition<AutoConceptCycleState, AutoConceptCycleAction>
{
    public AutoConceptCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoConceptCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoConceptCycleState state) => state = default;

    public WakePolicy Evaluate(
        in SuiteRuntimeConfiguration config,
        GameWorldState world,
        SuiteStrategy strategy,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        ServiceActionWriter<AutoConceptCycleAction> actions)
    {
        if (state.Lifecycle != context.Identity.Lifecycle)
            throw new InvalidOperationException("Auto Concept state belongs to a different lifecycle.");
        var wake = AutoConceptCycleEvaluator.Evaluate(
            world, in config, in context, ref state, actions, out var decision);
        state.RecordDecision(in decision);
        return wake;
    }

    public void ProjectState(
        in AutoConceptCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoConceptServiceProjection.Write(in state, output);
}
