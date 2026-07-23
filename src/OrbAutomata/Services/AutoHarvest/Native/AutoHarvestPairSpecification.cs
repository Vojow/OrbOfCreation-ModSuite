using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoHarvestPairSpecification
{
    private static readonly AutoHarvestPairSpecification Fruit = new(
        AutoHarvestPair.FruitTree,
        KnownEntities.FruitTreePlot,
        KnownEntities.FruitTreeCollect,
        KnownEntities.FruitTreeRewardPool,
        expectedGrowthSeconds: 480.0,
        expectedRestSeconds: 340.0,
        expectedActionSeconds: 3.0);

    private static readonly AutoHarvestPairSpecification Treasure = new(
        AutoHarvestPair.TreasureTree,
        KnownEntities.TreasureTreePlot,
        KnownEntities.TreasureTreeCollect,
        KnownEntities.TreasureTreeRewardPool,
        expectedGrowthSeconds: 720.0,
        expectedRestSeconds: 360.0,
        expectedActionSeconds: 10.0);

    private AutoHarvestPairSpecification(
        AutoHarvestPair pair,
        KnownEntity<PlotNodeSOContract> plot,
        KnownEntity<PlotNodeActionSOContract> action,
        KnownEntity<TreasurePoolSOContract> rewardPool,
        double expectedGrowthSeconds,
        double expectedRestSeconds,
        double expectedActionSeconds)
    {
        Pair = pair;
        Plot = plot;
        Action = action;
        RewardPool = rewardPool;
        ExpectedGrowthSeconds = expectedGrowthSeconds;
        ExpectedRestSeconds = expectedRestSeconds;
        ExpectedActionSeconds = expectedActionSeconds;
        PlotUuid = plot.Uuid.ToString("D");
        ActionUuid = action.Uuid.ToString("D");
    }

    internal AutoHarvestPair Pair { get; }
    internal KnownEntity<PlotNodeSOContract> Plot { get; }
    internal KnownEntity<PlotNodeActionSOContract> Action { get; }
    internal KnownEntity<TreasurePoolSOContract> RewardPool { get; }
    internal double ExpectedGrowthSeconds { get; }
    internal double ExpectedRestSeconds { get; }
    internal double ExpectedActionSeconds { get; }
    internal string PlotUuid { get; }
    internal string ActionUuid { get; }

    internal static AutoHarvestPairSpecification For(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => Fruit,
        AutoHarvestPair.TreasureTree => Treasure,
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };
}
