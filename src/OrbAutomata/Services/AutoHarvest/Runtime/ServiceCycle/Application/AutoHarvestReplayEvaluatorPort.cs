using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class AutoHarvestReplayEvaluatorPort : IServiceCycleReplayEvaluatorPort<
    AutoHarvestCycleFrame,
    AutomataConfiguration,
    AutoHarvestCycleState,
    AutoHarvestCycleAction,
    AutoHarvestStateRecord,
    AutoHarvestActionRecord>
{
    private readonly AutoHarvestCycleEvaluator _evaluator = new();

    public AutoHarvestCycleState CreateState(LifecycleGeneration lifecycle) =>
        AutoHarvestCycleState.Create(lifecycle);

    public void ReleaseState(ref AutoHarvestCycleState state) => state = default;
    public void ReleaseFrame(ref AutoHarvestCycleFrame frame) => frame = default;
    public AutoHarvestStateRecord CreateStateRecord(in AutoHarvestCycleState state) => new(state);

    public WakePolicy Evaluate(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config,
        in ServiceCycleContext context,
        ref AutoHarvestCycleState state,
        ServiceCycleReplayActionWriter<AutoHarvestCycleAction, AutoHarvestActionRecord> actions)
    {
        var result = _evaluator.Evaluate(frame, config, state, context);
        state = result.State;
        if (result.HasAction)
        {
            var action = result.Action;
            var record = new AutoHarvestActionRecord(action);
            actions.Add(action, record);
        }
        return result.Wake;
    }

    public void ProjectState(
        in AutoHarvestCycleState state,
        in ServiceProjectionContext context,
        ServiceStateProjectionBuilder output) =>
        AutoHarvestServiceProjection.Write(state, output);
}
