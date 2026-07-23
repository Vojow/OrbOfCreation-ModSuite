using System;

namespace OrbAutomata;

internal readonly struct AutoHarvestCycleAction
{
    public AutoHarvestCycleAction(AutoHarvestPair pair)
    {
        if (pair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            throw new ArgumentOutOfRangeException(nameof(pair));
        Pair = pair;
    }

    public AutoHarvestPair Pair { get; }
}
