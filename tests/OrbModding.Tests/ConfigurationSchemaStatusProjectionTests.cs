using System.Linq;
using System.Threading;
using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ConfigurationSchemaStatusProjectionTests
{
    [Theory]
    [InlineData(ConfigurationSchemaState.Current, 1, 1, true, true, false,
        "Configuration schema: Current 1; saved: Yes; loaded: Yes.")]
    [InlineData(ConfigurationSchemaState.Migrated, 0, 1, true, true, true,
        "Configuration schema: Migrated 0 to 1; saved: Yes; loaded: Yes; backup created: Yes. migrated")]
    [InlineData(ConfigurationSchemaState.Failed, 0, 1, false, false, false,
        "Configuration schema: Failed 0 to 1; saved: No; loaded: No. failed")]
    [InlineData(ConfigurationSchemaState.Future, 2, 1, true, false, false,
        "Configuration schema: Future 2; supported: 1; saved: Yes; loaded: No. future")]
    public void ProjectionReportsSavedAndLoadedSeparatelyFromRuntimeHealth(
        ConfigurationSchemaState state,
        int from,
        int to,
        bool saved,
        bool loaded,
        bool backup,
        string expected)
    {
        const string pluginId = "exact.plugin.guid";
        var registry = new ConfigurationSchemaStatusRegistry();
        registry.Publish(new ConfigurationSchemaStatus(
            pluginId,
            state,
            from,
            to,
            saved,
            loaded,
            state.ToString().ToLowerInvariant(),
            backup));

        Assert.Equal(expected, ConfigurationSchemaStatusProjection.Build(pluginId, registry).Text);
        Assert.Equal(
            "Configuration schema: Not reported; saved: Unknown; loaded: Unknown.",
            ConfigurationSchemaStatusProjection.Build("different.guid", registry).Text);
    }

    [Fact]
    public void SupportedPluginMarkersAreHiddenAndMarkerOnlySchemasReportNoSteps()
    {
        var mentorFile = new ConfigFile();
        var mentor = MentorConfig.TryBind(mentorFile);
        var modConfigFile = new ConfigFile();
        var modConfig = ModConfigSettings.TryBind(modConfigFile);
        var automataFile = new ConfigFile();
        var automata = BepInExAutomataConfiguration.TryBind(automataFile);

        Assert.True(mentor.Success);
        Assert.True(modConfig.Success);
        Assert.True(automata.Success);
        Assert.Empty(mentor.Diagnostics);
        Assert.Empty(modConfig.Diagnostics);
        AssertMarkerHidden(mentorFile);
        AssertMarkerHidden(modConfigFile);
        AssertMarkerHidden(automataFile);
    }

    [Fact]
    public void ModConfigCatalogAndEditSessionStartFromPostMigrationValues()
    {
        var file = new ConfigFile();
        file.SeedSerialized("AutoConcept", "Mode", "BalanceMastery");

        var migration = BepInExAutomataConfiguration.TryBind(file);
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource(PluginIds.AutomataGuid, PluginIds.AutomataName, PluginIds.AutomataVersion, file),
        });
        var session = new ConfigEditSession(catalog);

        Assert.True(migration.Success);
        Assert.Equal(AutoConceptOperationMode.Active, migration.Config!.AutoConceptMode.Value);
        Assert.DoesNotContain(catalog.Mods.SelectMany(mod => mod.Sections).SelectMany(section => section.Settings),
            setting => setting.Section == ConfigurationSchemaTransaction.MarkerSection);
        Assert.Equal(
            "Active",
            session.Values.Single(value =>
                value.Setting.SourceSection == "AutoConcept" && value.Setting.Key == "Mode").StagedSerialized);
        Assert.False(session.IsDirty);
    }

    [Theory]
    [InlineData(ConfigurationSchemaState.Current)]
    [InlineData(ConfigurationSchemaState.Migrated)]
    [InlineData(ConfigurationSchemaState.Failed)]
    [InlineData(ConfigurationSchemaState.Future)]
    public void ZeroSettingPluginIsRetainedForEveryExactSchemaState(ConfigurationSchemaState state)
    {
        const string pluginId = "exact.status-only.plugin";
        var registry = new ConfigurationSchemaStatusRegistry();
        registry.Publish(new ConfigurationSchemaStatus(
            pluginId,
            state,
            state == ConfigurationSchemaState.Current ? 1 : 0,
            1,
            state is ConfigurationSchemaState.Current or ConfigurationSchemaState.Migrated,
            state is ConfigurationSchemaState.Current or ConfigurationSchemaState.Migrated,
            "safe status",
            false));

        var snapshot = ConfigCatalog.Build(
            new[] { new ConfigPluginSource(pluginId, "Status Only", "1.0.0", new ConfigFile()) },
            registry);

        var mod = Assert.Single(snapshot.Mods);
        Assert.Equal(pluginId, mod.Guid);
        Assert.Empty(mod.Sections);
        Assert.Equal(0, snapshot.SettingCount);
    }

    [Fact]
    public void FailedAndFutureSuitePluginsRemainSelectableReadOnlyWhileUnreportedEmptyPluginIsOmitted()
    {
        var registry = new ConfigurationSchemaStatusRegistry();
        registry.Publish(new ConfigurationSchemaStatus(
            PluginIds.AutomataGuid,
            ConfigurationSchemaState.Failed,
            0,
            1,
            false,
            false,
            "failed safely",
            false));
        registry.Publish(new ConfigurationSchemaStatus(
            PluginIds.MentorGuid,
            ConfigurationSchemaState.Future,
            2,
            1,
            true,
            false,
            "future safely",
            false));

        var snapshot = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource(PluginIds.AutomataGuid, PluginIds.AutomataName, PluginIds.AutomataVersion, new ConfigFile()),
            new ConfigPluginSource(PluginIds.MentorGuid, PluginIds.MentorName, PluginIds.MentorVersion, new ConfigFile()),
            new ConfigPluginSource("third.party.empty", "Third Party Empty", "1.0.0", new ConfigFile()),
        }, registry);
        var session = new ConfigEditSession(snapshot);

        Assert.Equal(new[] { PluginIds.AutomataGuid, PluginIds.MentorGuid }, snapshot.Mods.Select(mod => mod.Guid));
        Assert.All(snapshot.Mods, mod =>
        {
            Assert.Empty(mod.Sections);
            Assert.False(ModConfigPanel.CanApplySelection(mod, sessionDirty: true, sessionValid: true));
        });
        Assert.Empty(session.Values);
        Assert.False(session.IsDirty);
        Assert.Equal(0, snapshot.SettingCount);
        Assert.DoesNotContain(snapshot.Mods, mod => mod.Guid == "third.party.empty");
        Assert.StartsWith("Configuration schema: Failed", ConfigurationSchemaStatusProjection.Build(PluginIds.AutomataGuid, registry).Text);
        Assert.StartsWith("Configuration schema: Future", ConfigurationSchemaStatusProjection.Build(PluginIds.MentorGuid, registry).Text);
    }

    [Fact]
    public void WorkerThreadSchemaTransitionOnlyMarksOneMainThreadRefresh()
    {
        var registry = new ConfigurationSchemaStatusRegistry();
        var latch = new ConfigurationSchemaDirtyLatch();
        var mainThread = Thread.CurrentThread.ManagedThreadId;
        var callbackThread = mainThread;
        var projectionCalls = 0;
        registry.Transitioned += _ =>
        {
            callbackThread = Thread.CurrentThread.ManagedThreadId;
            latch.MarkDirty();
        };

        var worker = new Thread(() => registry.Publish(new ConfigurationSchemaStatus(
                "exact.plugin.guid",
                ConfigurationSchemaState.Current,
                1,
                1,
                true,
                true,
                "current",
                false)))
        {
            IsBackground = true,
        };
        worker.Start();
        worker.Join();

        Assert.NotEqual(mainThread, callbackThread);
        Assert.Equal(mainThread, Thread.CurrentThread.ManagedThreadId);
        Assert.Equal(0, projectionCalls);
        Assert.True(latch.IsDirty);
        if (latch.TryConsume()) projectionCalls++;
        Assert.Equal(1, projectionCalls);
        Assert.False(latch.IsDirty);
        Assert.False(latch.TryConsume());
    }

    private static void AssertMarkerHidden(ConfigFile file)
    {
        var marker = Assert.Single(file, pair =>
            pair.Key.Section == ConfigurationSchemaTransaction.MarkerSection &&
            pair.Key.Key == ConfigurationSchemaTransaction.MarkerKey);
        Assert.Contains(marker.Value.Description.Tags, tag => tag is ModConfigMetadata { Hidden: true });
    }
}
