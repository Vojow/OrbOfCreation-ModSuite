using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;

namespace OrbAutomata;

internal readonly struct AutoHarvestActionRecord : IServiceCycleReplayRecord
{
    public AutoHarvestActionRecord(in AutoHarvestCycleAction action) => Pair = action.Pair;
    internal AutoHarvestActionRecord(AutoHarvestPair pair) => Pair = pair;
    public AutoHarvestPair Pair { get; }
}
