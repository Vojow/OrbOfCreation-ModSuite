using System;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Replay.Recording;

namespace OrbAutomata;

internal sealed class AutoHarvestReplayHydrator : IServiceCycleReplayHydrator<
    AutoHarvestCycleFrame,
    AutomataConfiguration,
    AutoHarvestCycleState,
    AutoHarvestCycleInputRecord,
    AutoHarvestStateRecord>
{
    public void HydrateFrame(
        in AutoHarvestCycleInputRecord input,
        in ServiceCycleReplayContext context,
        ref AutoHarvestCycleFrame frame) =>
        frame = input.ToFrame();

    public AutomataConfiguration HydrateConfiguration(
        in AutoHarvestCycleInputRecord input,
        in ServiceCycleReplayContext context) =>
        input.ToConfiguration();

    public AutoHarvestCycleState HydratePreviousState(
        in AutoHarvestStateRecord previousState,
        in ServiceCycleReplayContext context)
    {
        if (previousState.Lifecycle != context.Cycle.Lifecycle)
            throw new InvalidOperationException("Auto Harvest replay state belongs to another lifecycle.");
        return previousState.ToState();
    }

    public AutoHarvestCycleInputRecord RecreateCycleInputRecord(
        in AutoHarvestCycleFrame frame,
        in AutomataConfiguration config,
        in ServiceCycleReplayContext context) =>
        new(frame, config);
}
