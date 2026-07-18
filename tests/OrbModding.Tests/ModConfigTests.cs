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
            new[] { "Auto Buy", "Auto Cast", "Auto Concept", "Advanced" },
            mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections, section => section.Name == "Research" || section.Name == "ActiveMode");
        Assert.Equal(
            new[] { "Mode", "IncludeStructures", "IncludeUpgrades", "AutoLevelSpells", "AffordabilityMode", "UpgradeAffordabilityMode", "BatchSizingMode" },
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Take(7).Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "FullCharge", "ToggleShortcut", "ShowToggleButton", "EvaluationIntervalSeconds", "StartResourcePercent", "ManualPauseSeconds" },
            mod.Sections.Single(section => section.Name == "Auto Cast").Settings.Select(setting => setting.Key));
        Assert.Equal(
            new[] { "Mode", "SlotManagementMode", "ShowToggleButton", "TrainingPeriodSeconds", "PerConceptQuantityCap", "RateReservePercent", "MinimumResourcePercent", "MinimumDrainRatio", "AllowedUuids", "BlockedUuids" },
            mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Select(setting => setting.Key));
        Assert.DoesNotContain(
            mod.Sections.SelectMany(section => section.Settings),
            setting => setting.Key.Contains("RuntimeProbe", StringComparison.Ordinal) ||
                       setting.Key.Contains("PurchaseLimitPerSession", StringComparison.Ordinal));
        Assert.Contains(
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings,
            setting => setting.Key == "RespectActionMultiplier");
        Assert.Contains(
            mod.Sections.Single(section => section.Name == "Auto Buy").Settings,
            setting => setting.Key == "RepeatWhileAffordable");

        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        Assert.Contains(mod.Sections.Single(section => section.Name == "Advanced").Settings, setting => setting.Key == "EnableOperationalLogging");
        Assert.Contains(mod.Sections.Single(section => section.Name == "Advanced").Settings, setting => setting.Key == "FallbackEvaluationIntervalSeconds");

        var autoBuyMode = mod.Sections.Single(section => section.Name == "Auto Buy").Settings.Single(setting => setting.Key == "Mode");
        Assert.Equal(new[] { "Disabled", "Active" }, Enum.GetNames(autoBuyMode.SettingType));
        var autoConceptMode = mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Single(setting => setting.Key == "Mode");
        Assert.Equal(new[] { "Disabled", "Active" }, Enum.GetNames(autoConceptMode.SettingType));
        var autoConceptSlotManagement = mod.Sections.Single(section => section.Name == "Auto Concept").Settings.Single(setting => setting.Key == "SlotManagementMode");
        Assert.Equal(new[] { "RotateAll", "PreserveManual", "TimedCycle" }, Enum.GetNames(autoConceptSlotManagement.SettingType));

        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));
        var settings = mod.Sections.SelectMany(section => section.Settings).ToDictionary(
            setting => $"{setting.SourceSection}.{setting.Key}");

        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoBuy" && setting.Key != "Mode"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoBuy" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoCast" &&
                setting.Key is not ("Mode" or "ToggleShortcut" or "ShowToggleButton")),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoCast" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.All(
            settings.Values.Where(setting => setting.SourceSection == "AutoConcept" &&
                setting.Key is not ("Mode" or "ShowToggleButton")),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "AutoConcept" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));

        Assert.True(session.DependencySatisfied(settings["AutoCast.Mode"]));
        Assert.True(session.DependencySatisfied(settings["AutoCast.ToggleShortcut"]));
        Assert.True(session.DependencySatisfied(settings["AutoCast.ShowToggleButton"]));
        Assert.True(session.DependencySatisfied(settings["AutoBuy.AutoLevelSpells"]));
        session.Get(settings["AutoBuy.Mode"]).Stage("Disabled");
        Assert.False(session.DependencySatisfied(settings["AutoBuy.AutoLevelSpells"]));
        session.Get(settings["AutoBuy.Mode"]).Stage("Active");
        Assert.False(session.DependencySatisfied(settings["AutoCast.FullCharge"]));
        Assert.False(session.DependencySatisfied(settings["AutoConcept.SlotManagementMode"]));
        Assert.True(session.DependencySatisfied(settings["AutoConcept.ShowToggleButton"]));
        Assert.False(session.DependencySatisfied(settings["AutoConcept.FallbackEvaluationIntervalSeconds"]));

        session.Get(settings["AutoCast.Mode"]).Stage("Active");
        session.Get(settings["AutoConcept.Mode"]).Stage("Active");
        Assert.True(session.DependencySatisfied(settings["AutoCast.FullCharge"]));
        Assert.True(session.DependencySatisfied(settings["AutoConcept.SlotManagementMode"]));
        Assert.True(session.DependencySatisfied(settings["AutoConcept.FallbackEvaluationIntervalSeconds"]));

        Assert.False(session.DependencySatisfied(settings["AutoBuy.MaxPurchasesPerBatch"]));
        Assert.False(session.DependencySatisfied(settings["AutoBuy.FixedStructureLevelsPerCandidate"]));
        session.Get(settings["AutoBuy.BatchSizingMode"]).Stage("Fixed");
        session.Get(settings["AutoBuy.RepeatWhileAffordable"]).Stage("false");
        session.Get(settings["AutoBuy.StructureRepeatMode"]).Stage("Fixed");
        Assert.True(session.DependencySatisfied(settings["AutoBuy.MaxPurchasesPerBatch"]));
        Assert.True(session.DependencySatisfied(settings["AutoBuy.FixedStructureLevelsPerCandidate"]));
        session.Get(settings["AutoBuy.RespectActionMultiplier"]).Stage("true");
        Assert.False(session.DependencySatisfied(settings["AutoBuy.RepeatWhileAffordable"]));
        Assert.False(session.DependencySatisfied(settings["AutoBuy.StructureRepeatMode"]));
        Assert.False(session.DependencySatisfied(settings["AutoBuy.FixedStructureLevelsPerCandidate"]));
    }

    [Fact]
    public void MentorCatalog_UsesFeatureTabsAndDependencies()
    {
        var config = new ConfigFile();
        MentorConfig.Bind(config);
        var mod = ConfigCatalog.Build(new[] { new ConfigPluginSource("mentor", "Mentor", "test", config) }).Mods.Single();

        Assert.Equal(new[] { "Spells", "Artifacts", "Alchemy", "Advanced" }, mod.Sections.Select(section => section.Name));
        Assert.DoesNotContain(mod.Sections.SelectMany(section => section.Settings), setting => setting.SourceSection == "General" && setting.Key == "Enabled");
        var artifactShare = mod.Sections.Single(section => section.Name == "Artifacts").Settings.Single(setting => setting.Key == "SharePercent");
        Assert.Equal(2, artifactShare.Dependencies.Count);
        Assert.Contains(artifactShare.Dependencies, dependency => dependency.Section == "General" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active");
        Assert.Contains(artifactShare.Dependencies, dependency => dependency.Section == "Artifacts" && dependency.Key == "Enabled" && dependency.ExpectedValue == "true");

        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));
        var spellSection = mod.Sections.Single(section => section.Name == "Spells");
        var mentorMode = spellSection.Settings.Single(setting => setting.Key == "Mode");
        var toggleShortcut = spellSection.Settings.Single(setting => setting.Key == "ToggleShortcut");
        var artifactEnabled = mod.Sections.Single(section => section.Name == "Artifacts").Settings.Single(setting => setting.Key == "Enabled");
        Assert.All(
            mod.Sections.SelectMany(section => section.Settings).Where(setting =>
                setting.SourceSection is "Sharing" or "Artifacts" or "Alchemy" or "Performance"),
            setting => Assert.Contains(setting.Dependencies, dependency =>
                dependency.Section == "General" && dependency.Key == "Mode" && dependency.ExpectedValue == "Active"));
        Assert.False(session.DependencySatisfied(artifactShare));
        Assert.False(session.DependencySatisfied(artifactEnabled));
        Assert.True(session.DependencySatisfied(toggleShortcut));
        var dependencyMessage = session.DescribeUnsatisfiedDependencies(artifactShare);
        Assert.Contains("Mentor = Active", dependencyMessage);
        Assert.Contains("Artifact sharing = true", dependencyMessage);
        session.Get(mentorMode).Stage("Active");
        Assert.True(session.DependencySatisfied(artifactEnabled));
        Assert.False(session.DependencySatisfied(artifactShare));
        session.Get(artifactEnabled).Stage("true");
        Assert.True(session.DependencySatisfied(artifactShare));
    }

    [Fact]
    public void LegacySingleDependencyMetadataRemainsSupported()
    {
        var config = new ConfigFile();
        var enabled = config.Bind("Feature", "Enabled", false, "feature");
        config.Bind(
            "Feature",
            "Amount",
            5,
            new ConfigDescription(
                "amount",
                null,
                new ModConfigMetadata(
                    0,
                    10,
                    dependencySection: "Feature",
                    dependencyKey: "Enabled")));
        var mod = ConfigCatalog.Build(new[] { new ConfigPluginSource("legacy", "Legacy", "1", config) }).Mods.Single();
        var amount = mod.Sections.Single().Settings.Single(setting => setting.Key == "Amount");
        var session = new ConfigEditSession(new ConfigCatalogSnapshot(new[] { mod }));

        Assert.Single(amount.Dependencies);
        Assert.False(session.DependencySatisfied(amount));
        enabled.Value = true;
        session.RevertAll();
        Assert.True(session.DependencySatisfied(amount));
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
        config.Bind("AutoConcept", "AutoLevelSpells", false, "pre-release location");

        AutomataConfig.Bind(config);

        Assert.DoesNotContain(config, pair => pair.Key.Key.Contains("RuntimeProbe", StringComparison.Ordinal));
        Assert.DoesNotContain(config, pair => pair.Key.Key == "ActivePurchaseLimitPerSession");
        Assert.DoesNotContain(config, pair => pair.Key.Key == "AllowUnvalidatedActiveMode");
        Assert.DoesNotContain(config, pair => pair.Key.Section == "Research");
        Assert.DoesNotContain(config, pair => pair.Key.Section == "AutoConcept" && pair.Key.Key == "AutoLevelSpells");
        Assert.Contains(config, pair => pair.Key.Section == "AutoBuy" && pair.Key.Key == "AutoLevelSpells");
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

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_ApplyFailureRollsBackEveryWrittenEntryAndResavesOwner()
    {
        var config = new ConfigFile { ThrowOnSaveCall = 1 };
        var enabled = config.Bind("General", "Enabled", true, "Enabled");
        var count = config.Bind("General", "Count", 4, "Count");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);
        session.Get(settings["Enabled"]).Stage("false");
        session.Get(settings["Count"]).Stage("8");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Contains("simulated config save failure", error);
        Assert.True(enabled.Value);
        Assert.Equal(4, count.Value);
        Assert.Equal(2, config.SaveCalls);
        Assert.True(session.IsDirty);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_ThrowingSettingChangedSubscriberLeavesNoPartialCommit()
    {
        var config = new ConfigFile();
        var first = config.Bind("General", "First", 1, "First");
        var second = config.Bind("General", "Second", 2, "Second");
        second.SettingChanged += (_, _) => throw new InvalidOperationException("simulated subscriber failure");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("plugin", "Plugin", "1.0.0", config),
        });
        var session = new ConfigEditSession(catalog);
        var settings = catalog.Mods.Single().Sections.Single().Settings.ToDictionary(setting => setting.Key);
        session.Get(settings["First"]).Stage("10");
        session.Get(settings["Second"]).Stage("20");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Equal("simulated subscriber failure", error);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(1, config.SaveCalls);
        Assert.True(session.IsDirty);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void EditSession_MultipleConfigFilesRollbackAndResaveEveryOwner()
    {
        var firstConfig = new ConfigFile();
        var secondConfig = new ConfigFile { ThrowOnSaveCall = 1 };
        var first = firstConfig.Bind("General", "Value", 1, "Value");
        var second = secondConfig.Bind("General", "Value", 2, "Value");
        var catalog = ConfigCatalog.Build(new[]
        {
            new ConfigPluginSource("first", "First", "1.0.0", firstConfig),
            new ConfigPluginSource("second", "Second", "1.0.0", secondConfig),
        });
        var session = new ConfigEditSession(catalog);
        session.Values.Single(value => ReferenceEquals(value.Setting.Source.ConfigFile, firstConfig)).Stage("10");
        session.Values.Single(value => ReferenceEquals(value.Setting.Source.ConfigFile, secondConfig)).Stage("20");

        var applied = session.Apply(out var error);

        Assert.False(applied);
        Assert.Contains("simulated config save failure", error);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(2, firstConfig.SaveCalls);
        Assert.Equal(2, secondConfig.SaveCalls);
        Assert.True(session.IsDirty);
    }
}
