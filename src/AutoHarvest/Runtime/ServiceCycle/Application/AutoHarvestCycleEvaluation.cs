using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoHarvestCycleEvaluation
{
    public AutoHarvestCycleEvaluation(
        in AutoHarvestCycleState state,
        in AutoHarvestCycleAction action,
        bool hasAction,
        WakePolicy wake)
    {
        State = state;
        Action = action;
        HasAction = hasAction;
        Wake = wake;
    }

    public AutoHarvestCycleState State { get; }
    public AutoHarvestCycleAction Action { get; }
    public bool HasAction { get; }
    public WakePolicy Wake { get; }
}
