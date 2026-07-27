using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoHarvestPairSpecification
{
    private static readonly AutoHarvestPairSpecification Fruit = new(
        AutoHarvestPair.FruitTree,
        KnownEntities.FruitTreePlot,
        KnownEntities.FruitTreeCollect,
        KnownEntities.FruitTreeRewardPool);

    private static readonly AutoHarvestPairSpecification Treasure = new(
        AutoHarvestPair.TreasureTree,
        KnownEntities.TreasureTreePlot,
        KnownEntities.TreasureTreeCollect,
        KnownEntities.TreasureTreeRewardPool);

    private AutoHarvestPairSpecification(
        AutoHarvestPair pair,
        KnownEntity<PlotNodeSOContract> plot,
        KnownEntity<PlotNodeActionSOContract> action,
        KnownEntity<TreasurePoolSOContract> rewardPool)
    {
        Pair = pair;
        Plot = plot;
        Action = action;
        RewardPool = rewardPool;
        PlotUuid = plot.Uuid.ToString("D");
        ActionUuid = action.Uuid.ToString("D");
    }

    internal AutoHarvestPair Pair { get; }
    internal KnownEntity<PlotNodeSOContract> Plot { get; }
    internal KnownEntity<PlotNodeActionSOContract> Action { get; }
    internal KnownEntity<TreasurePoolSOContract> RewardPool { get; }
    internal string PlotUuid { get; }
    internal string ActionUuid { get; }

    internal static AutoHarvestPairSpecification For(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => Fruit,
        AutoHarvestPair.TreasureTree => Treasure,
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };
}
