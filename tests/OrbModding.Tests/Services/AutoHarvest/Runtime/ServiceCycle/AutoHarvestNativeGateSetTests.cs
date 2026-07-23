using Xunit;

namespace OrbAutomata.Tests;

public sealed class AutoHarvestNativeGateSetTests
{
    [Fact]
    public void MutationQuarantineSurvivesTheCurrentLifecycleAndClearsOnReplacement()
    {
        var gates = new AutoHarvestNativeGateSet();
        gates.ObserveLifecycle(4);
        gates.Quarantine(AutoHarvestPair.FruitTree);

        gates.ObserveLifecycle(4);
        Assert.True(gates.IsQuarantined(AutoHarvestPair.FruitTree));
        Assert.False(gates.IsQuarantined(AutoHarvestPair.TreasureTree));

        gates.ObserveLifecycle(3);
        Assert.True(gates.IsQuarantined(AutoHarvestPair.FruitTree));

        gates.ObserveLifecycle(5);
        Assert.False(gates.IsQuarantined(AutoHarvestPair.FruitTree));
    }
}
