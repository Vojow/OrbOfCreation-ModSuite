using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Performs an immediate Auto Agromancy-scoped world read on the Unity thread.
/// It never calls the planner or scans unrelated gameplay categories; the
/// resulting facts are used only for fingerprint and postcondition checks.
/// </summary>
internal sealed class AutoAgromancyLiveWorldReader : IAutoAgromancyLiveWorldReader
{
    private readonly GameWorldCollector _collector;
    private readonly GameWorldCycleFrame _frame = new();

    internal AutoAgromancyLiveWorldReader(GameWorldCollector? collector = null) =>
        _collector = collector ?? new GameWorldCollector();

    public bool TryRead(long lifecycleEpoch, out GameWorldState world)
    {
        world = GameWorldStateDefaults.Empty;
        if (lifecycleEpoch <= 0) return false;
        try
        {
            _frame.CollectedAtEpoch = lifecycleEpoch;
            var report = _collector.CollectAutoAgromancy(_frame);
            world = GameWorldFrameDeriver.Build(_frame);
            return report.IsComplete &&
                world.HarvestActionCaptureState ==
                WorldHarvestActionCaptureState.Complete;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            FormatException or OverflowException)
        {
            return false;
        }
    }
}
