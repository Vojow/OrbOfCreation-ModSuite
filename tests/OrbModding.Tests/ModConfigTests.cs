using System;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModConfig;
using OrbModding.Common;
using OrbMentor;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigTests
{
    private enum SampleMode
    {
        Disabled,
        Active,
    }

    [Fact]
    public void Catalog_GroupsAndSortsModsSectionsAndSettingsDeterministically()
    {
        var laterConfig = new ConfigFile();
        laterConfig.Bind("Zeta", "Second", true, "Second setting");
        laterConfig.Bind("Alpha", "Zulu", "value", "Last key");
        laterConfig.Bind("Alpha", "Alpha", 3, "First key");

        var earlierConfig = new ConfigFile();
        earlierConfig.Bind("General", "Enabled", true, "Enable it");

        var snapshot = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin.z", "Same Name", "2.0.0", laterConfig),
            new ConfigPluginSource("plugin.a", "Same Name", "1.0.0", earlierConfig),
        });

        Assert.Equal(new[] { "plugin.a", "plugin.z" }, snapshot.Mods.Select(mod => mod.Guid));
        Assert.Equal(new[] { "Alpha", "Zeta" }, snapshot.Mods[1].Sections.Select(section => section.Name));
        Assert.Equal(new[] { "Alpha", "Zulu" }, snapshot.Mods[1].Sections[0].Settings.Select(setting => setting.Key));
        Assert.Equal(4, snapshot.SettingCount);
    }

    [Fact]
    public void Catalog_ClassifiesSupportedEditorShapes()
    {
        var config = new ConfigFile();
        config.Bind("Types", "Boolean", true, "bool");
        config.Bind("Types", "Enum", SampleMode.Active, "enum");
        config.Bind(
            "Types",
            "Bounded",
            5,
            new ConfigDescription("bounded", new AcceptableValueRange<int>(1, 10)));
        config.Bind("Types", "Numeric", 1.5d, "numeric");
        config.Bind("Types", "String", "text", "string");
        config.Bind("Types", "Shortcut", new KeyboardShortcut(KeyCode.Equals), "shortcut");
        config.Bind("Types", "Unsupported", new object(), "unsupported");

        var settings = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
            })
            .Mods.Single()
            .Sections.Single()
            .Settings.ToDictionary(setting => setting.Key, setting => setting.Kind);

        Assert.Equal(ConfigEditorKind.Boolean, settings["Boolean"]);
        Assert.Equal(ConfigEditorKind.Enum, settings["Enum"]);
        Assert.Equal(ConfigEditorKind.BoundedNumeric, settings["Bounded"]);
        Assert.Equal(ConfigEditorKind.Numeric, settings["Numeric"]);
        Assert.Equal(ConfigEditorKind.String, settings["String"]);
        Assert.Equal(ConfigEditorKind.KeyboardShortcut, settings["Shortcut"]);
        Assert.Equal(ConfigEditorKind.Unsupported, settings["Unsupported"]);
    }

    [Fact]
    public void Catalog_UsesPresentationMetadataAndOmitsHiddenSettings()
    {
        var config = new ConfigFile();
        config.Bind(
            "Later",
            "First",
            true,
            new ConfigDescription("first", null, new ModConfigMetadata(20, 0)));
        config.Bind(
            "Earlier",
            "Second",
            true,
            new ConfigDescription("second", null, new ModConfigMetadata(10, 20)));
        config.Bind(
            "Earlier",
            "First",
            true,
            new ConfigDescription("first", null, new ModConfigMetadata(10, 10, displaySection: "Basics", displayName: "Friendly first")));
        config.Bind(
            "Earlier",
            "Hidden",
            true,
            new ConfigDescription("hidden", null, new ModConfigMetadata(10, 0, hidden: true)));

        var mod = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
            })
            .Mods.Single();

        Assert.Equal(new[] { "Basics", "Earlier", "Later" }, mod.Sections.Select(section => section.Name));
        Assert.Equal("Friendly first", mod.Sections[0].Settings.Single().DisplayName);
        Assert.Equal(3, mod.Sections.Sum(section => section.Settings.Count));
    }

    [Fact]
    public void AutomataCatalog_ShowsOnlyOrderedPublicConfiguration()
    {
        var config = new ConfigFile();
        AutomataConfig.Bind(config);

        var mod = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("automata", "Automata", "test", config),
            })
            .Mods.Single();

        Assert.Equal(
            new[] { "Auto Buy", "Auto Cast", "Advanced" },
            mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections, section => section.Name == "Research" || section.Name == "ActiveMode");
        Assert.Equal(
            new[] { "Mode", "IncludeStructures", "IncludeUpgrades", "AffordabilityMode", "UpgradeAffordabilityMode", "BatchSizingMode" },
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Take(6).Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "FullCharge", "ToggleShortcut", "ShowToggleButton", "EvaluationIntervalSeconds", "StartResourcePercent", "ManualPauseSeconds" },
            mod.Sections.Single(section => section.Name == "Auto Cast").Settings.Select(setting => setting.Key));
        Assert.DoesNotContain(
            mod.Sections.SelectMany(section => section.Settings),
            setting => setting.Key.Contains("RuntimeProbe", StringComparison.Ordinal) ||
                       setting.Key.Contains("PurchaseLimitPerSession", StringComparison.Ordinal));
        Assert.Contains(
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings,
            setting => setting.Key == "RespectActionMultiplier");

        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        Assert.Contains(mod.Sections.Single(section => section.Name == "Advanced").Settings, setting => setting.Key == "EnableOperationalLogging");

        var autoBuyMode = mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Single(setting => setting.Key == "Mode");
        Assert.Equal(new[] { "Disabled", "Active" }, Enum.GetNames(autoBuyMode.SettingType));
    }

    [Fact]
    public void MentorCatalog_UsesFeatureTabsAndDependencies()
    {
        var config = new ConfigFile();
        var mentor = MentorConfig.Bind(config);
        var mod = ConfigCatalog.Build(new[] { new ConfigPluginSource("mentor", "Mentor", "test", config) }).Mods.Single();

        Assert.Equal(new[] { "Spells", "Artifacts", "Alchemy", "Advanced" }, mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        var artifactShare = mod.Sections.Single(section => section.Name == "Artifacts").Settings.Single(setting => setting.Key == "SharePercent");
        Assert.Equal("Artifacts", artifactShare.DependencySection);
        Assert.Equal("Enabled", artifactShare.DependencyKey);

        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));
        Assert.False(session.DependencySatisfied(artifactShare));
        mentor.ArtifactsEnabled.Value = true;
        session.RevertAll();
        Assert.True(session.DependencySatisfied(artifactShare));
    }

    [Fact]
    public void AutomataBinding_RemovesDeprecatedReleaseSettings()
    {
        var config = new ConfigFile();
        config.Bind("AutoBuy", "RuntimeProbeConfirmed", true, "legacy");
        config.Bind("AutoBuy", "ActivePurchaseLimitPerSession", 1, "legacy");
        config.Bind("AutoCast", "RuntimeProbeConfirmed", true, "legacy");
        config.Bind("Safety", "AllowUnvalidatedActiveMode", false, "legacy");
        config.Bind("Research", "Mode", LegacyResearchAutomationMode.DryRun, "legacy");

        AutomataConfig.Bind(config);

        Assert.DoesNotContain(config, pair => pair.Key.Key.Contains("RuntimeProbe", StringComparison.Ordinal));
        Assert.DoesNotContain(config, pair => pair.Key.Key == "ActivePurchaseLimitPerSession");
        Assert.DoesNotContain(config, pair => pair.Key.Key == "AllowUnvalidatedActiveMode");
        Assert.DoesNotContain(config, pair => pair.Key.Section == "Research");
    }

    [Fact]
    public void Catalog_PreservesPresentationAndSerializedValues()
    {
        var config = new ConfigFile();
        var entry = config.Bind(
            "Performance",
            "BatchSize",
            8,
            new ConfigDescription("Purchases per queue fill.", new AcceptableValueRange<int>(1, 64)));
        entry.Value = 12;

        var setting = ConfigCatalog.Build(new[]
            {
                new ConfigPluginSource("plugin", "Plugin", "1.2.3", config),
            })
            .Mods.Single()
            .Sections.Single()
            .Settings.Single();

        Assert.Equal("Performance", setting.Section);
        Assert.Equal("BatchSize", setting.Key);
        Assert.Equal("Purchases per queue fill.", setting.Description);
        Assert.Equal("12", setting.CurrentSerializedValue);
        Assert.Equal("8", setting.DefaultSerializedValue);
        Assert.Contains("1", setting.AcceptableValuesDescription);
        Assert.Contains("64", setting.AcceptableValuesDescription);
    }

    [Fact]
    public void EditSession_StagesAppliesAndRevertsWithoutEarlyMutation()
    {
        var config = new ConfigFile();
        var enabled = config.Bind("General", "Enabled", true, "Enabled");
        var mode = config.Bind("General", "Mode", SampleMode.Disabled, "Mode");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);

        session.Get(settings["Enabled"]).Stage("false");
        session.Get(settings["Mode"]).Stage("Active");

        Assert.True(enabled.Value);
        Assert.Equal(SampleMode.Disabled, mode.Value);
        Assert.True(session.IsDirty);
        Assert.True(session.Apply(out var error), error);
        Assert.False(enabled.Value);
        Assert.Equal(SampleMode.Active, mode.Value);
        Assert.False(session.IsDirty);

        session.Get(settings["Enabled"]).Stage("true");
        session.RevertAll();
        Assert.Equal("False", session.Get(settings["Enabled"]).StagedSerialized);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void EditSession_RefreshesExternalChangesWithoutOverwritingStagedEdits()
    {
        var config = new ConfigFile();
        var mode = config.Bind("General", "Mode", SampleMode.Disabled, "Mode");
        var enabled = config.Bind("General", "Enabled", true, "Enabled");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);

        mode.Value = SampleMode.Active;
        session.Get(settings["Enabled"]).Stage("false");
        enabled.Value = false;

        Assert.True(session.RefreshExternalValues());
        Assert.Equal("Active", session.Get(settings["Mode"]).StagedSerialized);
        Assert.Equal("false", session.Get(settings["Enabled"]).StagedSerialized, ignoreCase: true);
        Assert.True(session.Get(settings["Enabled"]).IsDirty);
    }

    [Fact]
    public void EditSession_RejectsInvalidAndOutOfRangeValues()
    {
        var config = new ConfigFile();
        config.Bind("General", "Enabled", true, "Enabled");
        config.Bind(
            "General",
            "Count",
            4,
            new ConfigDescription("Count", new AcceptableValueRange<int>(1, 8)));
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);

        session.Get(settings["Enabled"]).Stage("sometimes");
        Assert.False(session.IsValid);
        Assert.False(session.Apply(out _));

        session.Get(settings["Enabled"]).Stage("true");
        session.Get(settings["Count"]).Stage("12");
        Assert.False(session.IsValid);
        Assert.Contains("Range", session.Get(settings["Count"]).Error);
    }
}
