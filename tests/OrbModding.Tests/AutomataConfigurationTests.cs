using BepInEx.Configuration;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataConfigurationTests
{
    [Fact]
    public void CapturesOneImmutableValueSetAcrossEveryConfigurationSection()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.Enabled.Value = false;
        config.AutoBuyStructures.Value = false;
        config.AutoCastStartResourcePercent.Value = 37;
        config.AutoConceptQuantityCap.Value = 19;
        config.AutoHarvestTreasureTrees.Value = false;
        config.EmergencyDisable.Value = true;
        config.CpuBudgetMilliseconds.Value = 0.5f;
        config.EnableOperationalLogging.Value = true;
        config.Replay.EnableAutoHarvestCapture.Value = true;
        config.AbsoluteReserve.Value = "42";

        var snapshot = config.Current;

        Assert.False(snapshot.General.Enabled);
        Assert.False(snapshot.AutoBuy.IncludeStructures);
        Assert.Equal(37, snapshot.AutoCast.StartResourcePercent);
        Assert.Equal(19, snapshot.AutoConcept.QuantityCap);
        Assert.False(snapshot.AutoHarvest.CollectTreasureTrees);
        Assert.True(snapshot.Safety.EmergencyDisable);
        Assert.Equal(0.5f, snapshot.Performance.CpuBudgetMilliseconds);
        Assert.True(snapshot.Diagnostics.EnableOperationalLogging);
        Assert.True(snapshot.Replay.EnableAutoHarvestCapture);
        Assert.Equal("42", snapshot.Reserves.AbsoluteReserve);

        config.Enabled.Value = true;
        config.AbsoluteReserve.Value = "100";

        Assert.False(snapshot.General.Enabled);
        Assert.Equal("42", snapshot.Reserves.AbsoluteReserve);
        Assert.NotSame(snapshot, config.Current);
        Assert.True(config.Current.General.Enabled);
        Assert.Equal("100", config.Current.Reserves.AbsoluteReserve);
    }
}
