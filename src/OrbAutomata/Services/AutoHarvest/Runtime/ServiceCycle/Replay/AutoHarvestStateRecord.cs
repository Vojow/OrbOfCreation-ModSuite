using OrbModding.Common.Runtime.ServiceCycle.Replay.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal readonly struct AutoHarvestPairHealthRecord : IServiceCycleReplayRecord
{
    public AutoHarvestPairHealthRecord(in AutoHarvestPairHealth health)
    {
        Selected = health.Selected;
        Kind = health.Kind;
        FeatureScoped = health.FeatureScoped;
    }

    public bool Selected { get; }
    public AutoHarvestPairHealthKind Kind { get; }
    public bool FeatureScoped { get; }

    public AutoHarvestPairHealth ToHealth(AutoHarvestPair pair) =>
        new(pair, Selected, Kind, FeatureScoped);
}

internal readonly struct AutoHarvestStateRecord : IServiceCycleReplayRecord
{
    public AutoHarvestStateRecord(in AutoHarvestCycleState state)
    {
        Lifecycle = state.Lifecycle.Value;
        NextPair = state.NextPair;
        HasPlannedAction = state.HasPlannedAction;
        PlannedPair = state.PlannedPair;
        FruitHealth = new AutoHarvestPairHealthRecord(state.FruitHealth);
        TreasureHealth = new AutoHarvestPairHealthRecord(state.TreasureHealth);
    }

    internal AutoHarvestStateRecord(
        ulong lifecycle,
        AutoHarvestPair nextPair,
        bool hasPlannedAction,
        AutoHarvestPair plannedPair,
        in AutoHarvestPairHealthRecord fruitHealth,
        in AutoHarvestPairHealthRecord treasureHealth)
    {
        Lifecycle = lifecycle;
        NextPair = nextPair;
        HasPlannedAction = hasPlannedAction;
        PlannedPair = plannedPair;
        FruitHealth = fruitHealth;
        TreasureHealth = treasureHealth;
    }

    public ulong Lifecycle { get; }
    public AutoHarvestPair NextPair { get; }
    public bool HasPlannedAction { get; }
    public AutoHarvestPair PlannedPair { get; }
    public AutoHarvestPairHealthRecord FruitHealth { get; }
    public AutoHarvestPairHealthRecord TreasureHealth { get; }

    public AutoHarvestCycleState ToState() => AutoHarvestCycleState.Restore(
        new LifecycleGeneration(Lifecycle),
        NextPair,
        HasPlannedAction,
        PlannedPair,
        FruitHealth.ToHealth(AutoHarvestPair.FruitTree),
        TreasureHealth.ToHealth(AutoHarvestPair.TreasureTree));
}
