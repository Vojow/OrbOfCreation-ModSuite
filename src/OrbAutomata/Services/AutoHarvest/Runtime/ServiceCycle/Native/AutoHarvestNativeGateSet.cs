namespace OrbAutomata;

internal sealed class AutoHarvestNativeGateSet : IAutoHarvestGatePort
{
    private long _lifecycle;
    private bool _fruitQuarantined;
    private bool _treasureQuarantined;

    public void ObserveLifecycle(long lifecycle)
    {
        if (lifecycle <= _lifecycle) return;
        _lifecycle = lifecycle;
        _fruitQuarantined = false;
        _treasureQuarantined = false;
    }

    public void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs)
    {
        if (pairs.Fruit.Succeeded) ObserveLifecycle(pairs.Fruit.Pair.LifecycleGeneration);
        else if (pairs.Treasure.Succeeded) ObserveLifecycle(pairs.Treasure.Pair.LifecycleGeneration);
    }

    public bool IsQuarantined(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => _fruitQuarantined,
        AutoHarvestPair.TreasureTree => _treasureQuarantined,
        _ => true,
    };

    public void Quarantine(AutoHarvestPair pair)
    {
        if (pair == AutoHarvestPair.FruitTree) _fruitQuarantined = true;
        else if (pair == AutoHarvestPair.TreasureTree) _treasureQuarantined = true;
    }
}
