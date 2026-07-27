using BepInEx.Configuration;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataConfigurationTests
{
    /// <summary>
    /// A change from any source is a change to publish, and it is taken exactly once.
    /// </summary>
    /// <remarks>
    /// The suite used to publish off the invalidation its own settings panel raises, so a setting
    /// changed through BepInEx's configuration manager or by editing the file never advanced a
    /// generation and every service kept deciding against the previous reading. BepInEx raises one
    /// event whatever moved the setting, so taking the change from here covers all of them.
    /// </remarks>
    [Fact]
    public void EverySourceOfAChangeLeavesOneReadingToPublish()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        // Binding itself settles the entries, and that counts as a change like any other.
        config.TryTakeUnpublishedChange(out _);
        Assert.False(config.TryTakeUnpublishedChange(out _));

        config.AutoBuyIntervalSeconds.Value = 7f;

        Assert.True(config.TryTakeUnpublishedChange(out var changed));
        Assert.Equal(7f, changed.AutoBuy.EvaluationIntervalSeconds);
        Assert.Same(config.Current, changed);
        Assert.False(config.TryTakeUnpublishedChange(out _));
    }

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
        config.EnableOperationalLogging.Value = true;
        config.AbsoluteReserve.Value = "42";

        var snapshot = config.Current;

        Assert.False(snapshot.General.Enabled);
        Assert.False(snapshot.AutoBuy.IncludeStructures);
        Assert.Equal(37, snapshot.AutoCast.StartResourcePercent);
        Assert.Equal(19, snapshot.AutoConcept.QuantityCap);
        Assert.False(snapshot.AutoHarvest.CollectTreasureTrees);
        Assert.True(snapshot.Safety.EmergencyDisable);
        Assert.True(snapshot.Diagnostics.EnableOperationalLogging);
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
