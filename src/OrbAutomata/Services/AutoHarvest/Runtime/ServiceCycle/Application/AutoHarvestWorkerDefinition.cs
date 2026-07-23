using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;

namespace OrbAutomata;

internal sealed class AutoHarvestWorkerDefinition :
    IServiceCycleWorkerDefinition<
        AutoHarvestCycleFrame,
        AutomataConfiguration,
        AutoHarvestCycleState,
        AutoHarvestCycleAction>
{
    private readonly AutoHarvestCycleEvaluator _evaluator = new();

    public AutoHarvestCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoHarvestCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoHarvestCycleState state) => state = default;
    public void ReleaseFrame(ref AutoHarvestCycleFrame frame) => frame = default;

    public WakePolicy Evaluate(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config,
        in ServiceCycleContext context,
        ref AutoHarvestCycleState state,
        ServiceActionWriter<AutoHarvestCycleAction> actions)
    {
        var result = _evaluator.Evaluate(frame, config, state, context);
        state = result.State;
        if (result.HasAction)
        {
            var action = result.Action;
            actions.Add(in action);
        }
        return result.Wake;
    }

    public void ProjectState(
        in AutoHarvestCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoHarvestServiceProjection.Write(state, output);
}
