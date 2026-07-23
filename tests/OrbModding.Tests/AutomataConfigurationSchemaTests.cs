using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataConfigurationSchemaTests
{
    [Fact]
    public void MalformedLegacyModeRestoresExactFileAndReturnsNoRuntimeConfig()
    {
        var path = "config/automata-malformed-mode.cfg";
        var original = new byte[] { 0, 4, 8, 15, 16, 23, 42, 255 };
        var files = new FakeFileOperations((path, original));
        var config = new ConfigFile(path);
        config.SeedSerialized("AutoConcept", "Mode", "NotAReviewedMode");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.False(result.Status.Loaded);
        Assert.Contains("mode", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(path, result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 3)));
        Assert.Empty(config);
        Assert.True(config.ReloadCalls >= 1);
    }

    [Theory]
    [InlineData("LegacyController")]
    [InlineData("KernelPilot")]
    [InlineData("arbitrary obsolete value")]
    public void EarlierRuntimeSelectorIsInertAndIgnoredWithoutValidation(string serialized)
    {
        var config = new ConfigFile();
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "1");
        config.SeedSerialized("AutoHarvest", "RuntimeImplementation", serialized);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(1, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.True(config.TryGetPersisted("AutoHarvest", "RuntimeImplementation", out var persisted));
        Assert.Equal(serialized, persisted);
        Assert.DoesNotContain(config, entry =>
            entry.Key.Section == "AutoHarvest" && entry.Key.Key == "RuntimeImplementation");
        Assert.True(config.TryGetPersisted(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            out var marker));
        Assert.Equal("3", marker);
    }

    [Fact]
    public void CurrentSchemaRuntimeSelectorIsInertAndRequiresNoMigration()
    {
        var config = new ConfigFile();
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "3");
        config.SeedSerialized("AutoHarvest", "RuntimeImplementation", "unrecognized old value");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Current, result.Status.State);
        Assert.Equal(3, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.True(config.TryGetPersisted("AutoHarvest", "RuntimeImplementation", out var persisted));
        Assert.Equal("unrecognized old value", persisted);
        Assert.DoesNotContain(config, entry =>
            entry.Key.Section == "AutoHarvest" && entry.Key.Key == "RuntimeImplementation");
    }

    [Fact]
    public void VersionOneMigrationUsesV3BackupSuffixAndPersistsV3Marker()
    {
        const string path = "config/automata-v1.cfg";
        var original = Encoding.UTF8.GetBytes("schema-one-exact-bytes");
        var files = new FakeFileOperations((path, original));
        var config = new ConfigFile(path);
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "1");
        config.SeedSerialized("AutoHarvest", "RuntimeImplementation", "KernelPilot");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        var backupPath = ConfigurationSchemaTransaction.GetBackupPath(path, 3);
        Assert.EndsWith(".pre-schema-v3.bak", backupPath, StringComparison.Ordinal);
        Assert.Equal(original, files.ReadAllBytes(backupPath));
        Assert.True(config.TryGetPersisted(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            out var marker));
        Assert.Equal("3", marker);
    }

    [Fact]
    public void VersionZeroRunsOrderedMigrationChainThroughVersionThree()
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "Mode", "BalanceMastery");
        config.SeedSerialized("AutoConcept", "RebalanceIntervalMinutes", "1");
        config.SeedSerialized("AutoHarvest", "RuntimeImplementation", "kernelpilot");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(0, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.Equal(AutoConceptOperationMode.Active, result.Config!.AutoConceptMode.Value);
        Assert.Equal(60, result.Config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.True(config.TryGetPersisted("AutoHarvest", "RuntimeImplementation", out var persisted));
        Assert.Equal("kernelpilot", persisted);
        Assert.DoesNotContain(config, entry =>
            entry.Key.Section == "AutoHarvest" && entry.Key.Key == "RuntimeImplementation");
        Assert.True(config.TryGetPersisted(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            out var marker));
        Assert.Equal("3", marker);
    }

    [Fact]
    public void FutureVersionFourRemainsReadOnlyWithoutBindingSavingOrBackup()
    {
        const string path = "config/automata-v4.cfg";
        var original = Encoding.UTF8.GetBytes("future-version-four");
        var files = new FakeFileOperations((path, original));
        var config = new ConfigFile(path);
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "4");
        config.SeedSerialized("AutoHarvest", "RuntimeImplementation", "KernelPilot");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.Equal(ConfigurationSchemaState.Future, result.Status.State);
        Assert.Equal(4, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.True(result.Status.Saved);
        Assert.False(result.Status.Loaded);
        Assert.Equal(0, config.SaveCalls);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.False(files.Exists(ConfigurationSchemaTransaction.GetBackupPath(path, 3)));
    }

    [Fact]
    public void AllRemovalListKeysAreExplicitDiscardedObsoleteDiagnostics()
    {
        var config = new ConfigFile();
        foreach (var key in AutomataConfigurationSchema.DiscardedObsoleteKeys)
            config.SeedSerialized(key.Section, key.Key, "opaque legacy value");
        config.SeedSerialized("ThirdParty", "Orphan", "preserved");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        var discarded = result.Diagnostics
            .Where(item => item.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete)
            .Select(item => item.Source)
            .ToHashSet();
        Assert.Equal(AutomataConfigurationSchema.DiscardedObsoleteKeys.Count, discarded.Count);
        Assert.Contains(new ConfigurationKey("AutoConcept", "AutoLevelSpells"), discarded);
        Assert.Contains(new ConfigurationKey("Performance", "MaxCandidatesPerEvaluation"), discarded);
        Assert.True(config.TryGetPersisted("ThirdParty", "Orphan", out var orphan));
        Assert.Equal("preserved", orphan);
    }

    [Theory]
    [InlineData("0", 10)]
    [InlineData("10", 10)]
    [InlineData("1801", 1800)]
    public void LegacySecondsUseInvariantNonNegativeParsingAndClamp(string serialized, int expected)
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "RebalanceIntervalSeconds", serialized);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(expected, result.Config!.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.Contains(result.Diagnostics, item =>
            item.Kind == ConfigurationMigrationDiagnosticKind.Mapped &&
            item.Source == AutomataConfigurationSchema.PreviousIntervalSeconds &&
            item.Destination == AutomataConfigurationSchema.FallbackInterval);
    }

    [Theory]
    [InlineData("0.175", 11)]
    [InlineData("0.1", 10)]
    [InlineData("30", 1800)]
    public void LegacyMinutesRoundAwayFromZeroThenClamp(string serialized, int expected)
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "RebalanceIntervalMinutes", serialized);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(expected, result.Config!.AutoConceptFallbackEvaluationIntervalSeconds.Value);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("not-a-number")]
    public void MalformedOrNegativeLegacySecondsFailClosed(string serialized)
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "RebalanceIntervalSeconds", serialized);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")]
    public void NegativeNonFiniteOrNonInvariantLegacyMinutesFailClosed(string serialized)
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "RebalanceIntervalMinutes", serialized);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
    }

    [Fact]
    public void DestinationPrecedenceIgnoresMalformedSupersededSourcesAndReportsDiscards()
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "FallbackEvaluationIntervalSeconds", "45");
        config.SeedSerialized("AutoConcept", "RebalanceIntervalSeconds", "malformed");
        config.SeedSerialized("AutoConcept", "RebalanceIntervalMinutes", "also-malformed");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(45, result.Config!.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.Contains(result.Diagnostics, item =>
            item.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete &&
            item.Source == AutomataConfigurationSchema.PreviousIntervalSeconds);
        Assert.Contains(result.Diagnostics, item =>
            item.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete &&
            item.Source == AutomataConfigurationSchema.LegacyIntervalMinutes);
    }

    [Fact]
    public void PartialVersionZeroConfigurationUsesDefaultsForMissingKnownValues()
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoConcept", "Mode", "active");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(AutoConceptOperationMode.Active, result.Config!.AutoConceptMode.Value);
        Assert.Equal(300, result.Config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
    }

    [Theory]
    [InlineData("false", "Single", "7", 0)]
    [InlineData("false", "Fixed", "7", 1)]
    [InlineData("false", "BulkDevelopment", "7", 2)]
    [InlineData("true", "Single", "7", 3)]
    public void VersionTwoPurchaseSettingsCollapseIntoOneGroupingMode(
        string respectActionMultiplier,
        string structureRepeatMode,
        string fixedGroupSize,
        int expectedGrouping)
    {
        var config = new ConfigFile();
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "2");
        config.SeedSerialized("AutoBuy", "RespectActionMultiplier", respectActionMultiplier);
        config.SeedSerialized("AutoBuy", "RepeatWhileAffordable", "true");
        config.SeedSerialized("AutoBuy", "StructureRepeatMode", structureRepeatMode);
        config.SeedSerialized("AutoBuy", "FixedStructureLevelsPerCandidate", fixedGroupSize);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(2, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.Equal((AutoBuyPurchaseGroupingMode)expectedGrouping, result.Config!.PurchaseGrouping.Value);
        Assert.Equal(7, result.Config.FixedGroupSize.Value);
        Assert.Contains(result.Diagnostics, item =>
            item.Kind == ConfigurationMigrationDiagnosticKind.DiscardedObsolete &&
            item.Source == AutomataConfigurationSchema.LegacyRepeatWhileAffordable);
    }

    [Fact]
    public void ExistingPurchaseGroupingHasPrecedenceOverLegacySettings()
    {
        var config = new ConfigFile();
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "2");
        config.SeedSerialized("AutoBuy", "PurchaseGrouping", "Fixed");
        config.SeedSerialized("AutoBuy", "FixedGroupSize", "9");
        config.SeedSerialized("AutoBuy", "RespectActionMultiplier", "true");
        config.SeedSerialized("AutoBuy", "StructureRepeatMode", "BulkDevelopment");
        config.SeedSerialized("AutoBuy", "FixedStructureLevelsPerCandidate", "3");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(AutoBuyPurchaseGroupingMode.Fixed, result.Config!.PurchaseGrouping.Value);
        Assert.Equal(9, result.Config.FixedGroupSize.Value);
    }

    [Fact]
    public void SchemaZeroPurchaseSettingsRunThroughAllMigrationSteps()
    {
        var config = new ConfigFile();
        config.SeedSerialized("AutoBuy", "RespectActionMultiplier", "false");
        config.SeedSerialized("AutoBuy", "RepeatWhileAffordable", "true");
        config.SeedSerialized("AutoBuy", "StructureRepeatMode", "Fixed");
        config.SeedSerialized("AutoBuy", "FixedStructureLevelsPerCandidate", "6");

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(0, result.Status.FromVersion);
        Assert.Equal(3, result.Status.ToVersion);
        Assert.Equal(AutoBuyPurchaseGroupingMode.Fixed, result.Config!.PurchaseGrouping.Value);
        Assert.Equal(6, result.Config.FixedGroupSize.Value);
    }

    [Theory]
    [InlineData("not-a-bool", "Single", "2")]
    [InlineData("false", "Unknown", "2")]
    [InlineData("false", "Fixed", "0")]
    [InlineData("false", "Fixed", "101")]
    public void MalformedVersionTwoPurchaseSettingsFailClosed(
        string respectActionMultiplier,
        string structureRepeatMode,
        string fixedGroupSize)
    {
        var config = new ConfigFile();
        config.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "2");
        config.SeedSerialized("AutoBuy", "RespectActionMultiplier", respectActionMultiplier);
        config.SeedSerialized("AutoBuy", "StructureRepeatMode", structureRepeatMode);
        config.SeedSerialized("AutoBuy", "FixedStructureLevelsPerCandidate", fixedGroupSize);

        var result = BepInExAutomataConfiguration.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Null(result.Config);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
    }

    private sealed class FakeFileOperations : IConfigurationFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public FakeFileOperations(params (string Path, byte[] Contents)[] files)
        {
            foreach (var file in files) _files[file.Path] = file.Contents.ToArray();
        }

        public bool Exists(string path) => _files.ContainsKey(path);

        public byte[] ReadAllBytes(string path) => _files[path].ToArray();

        public void WriteAllBytes(string path, byte[] contents) => _files[path] = contents.ToArray();

        public void Delete(string path) => _files.Remove(path);

        public ConfigurationBackupCreationResult CreateNewBackup(string path, byte[] contents)
        {
            if (_files.ContainsKey(path)) return ConfigurationBackupCreationResult.Collision;
            _files[path] = contents.ToArray();
            return ConfigurationBackupCreationResult.Created;
        }
    }
}
