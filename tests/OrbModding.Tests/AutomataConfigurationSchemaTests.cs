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

        var result = AutomataConfig.TryBind(
            config,
            files,
            new ConfigurationSchemaStatusRegistry());

        Assert.False(result.Success);
        Assert.Equal(ConfigurationSchemaState.Failed, result.Status.State);
        Assert.False(result.Status.Loaded);
        Assert.Contains("mode", result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(path, result.Status.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, files.ReadAllBytes(path));
        Assert.Equal(original, files.ReadAllBytes(ConfigurationSchemaTransaction.GetBackupPath(path, 1)));
        Assert.Empty(config);
        Assert.True(config.ReloadCalls >= 1);
    }

    [Fact]
    public void AllRemovalListKeysAreExplicitDiscardedObsoleteDiagnostics()
    {
        var config = new ConfigFile();
        foreach (var key in AutomataConfigurationSchema.DiscardedObsoleteKeys)
            config.SeedSerialized(key.Section, key.Key, "opaque legacy value");
        config.SeedSerialized("ThirdParty", "Orphan", "preserved");

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
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

        var result = AutomataConfig.TryBind(
            config,
            new FakeFileOperations(),
            new ConfigurationSchemaStatusRegistry());

        Assert.True(result.Success);
        Assert.Equal(AutoConceptOperationMode.Active, result.Config!.AutoConceptMode.Value);
        Assert.Equal(300, result.Config.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
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
