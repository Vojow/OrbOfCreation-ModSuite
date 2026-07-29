using System.Collections.Generic;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataConfigurationTests
{
    [Fact]
    public void ConstructionAbsorbsBindingStateWithoutRepublishingIt()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var publications = 0;

        var store = new AutomataConfigurationStore(config, (_, _) => publications++);

        Assert.Same(config.Current, store.Current);
        Assert.Equal(new ConfigGeneration(1), store.CurrentGeneration);
        Assert.False(store.TryPublishPending());
        Assert.Equal(0, publications);
    }

    [Fact]
    public void QuickControlSynchronouslyConsumesTheOnePendingPublication()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.TryTakeUnpublishedChange(out _);
        var published = new List<(SuiteRuntimeConfiguration Snapshot, ConfigGeneration Generation)>();
        var changes = new AutomataConfigurationStore(
            config,
            (snapshot, generation) => published.Add((snapshot, generation)));
        var control = new AutoCastToggleControl(
            changes,
            () => throw new System.InvalidOperationException("Runtime status is not read by this test."));

        control.Toggle();

        var publication = Assert.Single(published);
        var snapshot = publication.Snapshot;
        Assert.Equal(AutoCastOperationMode.Active, snapshot.AutoCast.Mode);
        Assert.Same(config.Current, snapshot);
        Assert.Equal(new ConfigGeneration(1).Next(), publication.Generation);
        Assert.False(changes.TryPublishPending());
        Assert.Single(published);
    }

    [Fact]
    public void SavedReadingChangesOnlyWhenTheMainThreadPublishesIt()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.TryTakeUnpublishedChange(out _);
        var published = new List<SuiteRuntimeConfiguration>();
        var store = new AutomataConfigurationStore(
            config,
            (snapshot, _) => published.Add(snapshot));

        config.AutoCastMode.Value = AutoCastOperationMode.Active;

        Assert.Equal(AutoCastOperationMode.Disabled, store.Current.AutoCast.Mode);
        Assert.Empty(published);

        store.PublishPending();

        var snapshot = Assert.Single(published);
        Assert.Same(snapshot, store.Current);
        Assert.Equal(AutoCastOperationMode.Active, store.Current.AutoCast.Mode);
    }

    [Fact]
    public void QuickControlResolvesAgainstCommittedStateWhenAnExternalEditIsPending()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var published = new List<SuiteRuntimeConfiguration>();
        var store = new AutomataConfigurationStore(
            config,
            (snapshot, _) => published.Add(snapshot));
        var control = new AutoCastToggleControl(
            store,
            () => throw new System.InvalidOperationException("Runtime status is not read by this test."));

        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        Assert.Equal(AutoCastOperationMode.Disabled, store.Current.AutoCast.Mode);
        Assert.Equal(AutoCastOperationMode.Active, config.Current.AutoCast.Mode);

        control.Toggle();

        var committed = Assert.Single(published);
        Assert.Same(committed, store.Current);
        Assert.Equal(AutoCastOperationMode.Active, committed.AutoCast.Mode);
        Assert.Equal(AutoCastToggleVisualState.On, control.State);
        Assert.False(store.TryPublishPending());
    }

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
        config.AbsoluteReserve.Value = "42";

        var snapshot = config.Current;

        Assert.False(snapshot.General.Enabled);
        Assert.False(snapshot.AutoBuy.IncludeStructures);
        Assert.Equal(37, snapshot.AutoCast.StartResourcePercent);
        Assert.Equal(19, snapshot.AutoConcept.QuantityCap);
        Assert.False(snapshot.AutoHarvest.CollectTreasureTrees);
        Assert.True(snapshot.Safety.EmergencyDisable);
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
