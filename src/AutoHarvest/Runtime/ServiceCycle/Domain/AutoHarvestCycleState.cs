using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal readonly struct AutoHarvestCycleState
{
    private AutoHarvestCycleState(
        LifecycleGeneration lifecycle,
        AutoHarvestPair nextPair,
        bool hasPlannedAction,
        AutoHarvestPair plannedPair,
        AutoHarvestPairHealth fruitHealth,
        AutoHarvestPairHealth treasureHealth,
        AutoHarvestFaultMemory faults)
    {
        Lifecycle = lifecycle;
        NextPair = nextPair;
        HasPlannedAction = hasPlannedAction;
        PlannedPair = plannedPair;
        FruitHealth = fruitHealth;
        TreasureHealth = treasureHealth;
        Faults = faults;
    }

    public LifecycleGeneration Lifecycle { get; }
    public AutoHarvestPair NextPair { get; }
    public bool HasPlannedAction { get; }
    public AutoHarvestPair PlannedPair { get; }
    public AutoHarvestPairHealth FruitHealth { get; }
    public AutoHarvestPairHealth TreasureHealth { get; }

    /// <summary>What this service's own past actions have told it about itself.</summary>
    public AutoHarvestFaultMemory Faults { get; }

    public static AutoHarvestCycleState Create(LifecycleGeneration lifecycle)
    {
        if (lifecycle.Value == 0) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        return new AutoHarvestCycleState(
            lifecycle,
            AutoHarvestPair.FruitTree,
            false,
            default,
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            default);
    }

    public static AutoHarvestCycleState Restore(
        LifecycleGeneration lifecycle,
        AutoHarvestPair nextPair,
        bool hasPlannedAction,
        AutoHarvestPair plannedPair,
        AutoHarvestPairHealth fruitHealth,
        AutoHarvestPairHealth treasureHealth,
        AutoHarvestFaultMemory faults)
    {
        if (lifecycle.Value == 0) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (nextPair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            throw new ArgumentOutOfRangeException(nameof(nextPair));
        if (hasPlannedAction &&
            plannedPair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            throw new ArgumentOutOfRangeException(nameof(plannedPair));
        return new AutoHarvestCycleState(
            lifecycle,
            nextPair,
            hasPlannedAction,
            hasPlannedAction ? plannedPair : default,
            fruitHealth,
            treasureHealth,
            faults);
    }

    public AutoHarvestCycleState CompleteEvaluation(
        AutoHarvestPair nextPair,
        AutoHarvestPairHealth fruitHealth,
        AutoHarvestPairHealth treasureHealth,
        bool hasAction,
        AutoHarvestPair actionPair,
        AutoHarvestFaultMemory faults) =>
        new(
            Lifecycle,
            nextPair,
            hasAction,
            hasAction ? actionPair : default,
            fruitHealth,
            treasureHealth,
            faults);
}
