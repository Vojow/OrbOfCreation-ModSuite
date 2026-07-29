using BepInEx.Configuration;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuiteConfigurationSchemaSixTests
{
    [Fact]
    public void SchemaSixMapsSerializedThreeHundredSecondFallbackToTenSeconds()
    {
        var file = VersionFiveFile("300");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(5, result.Status.FromVersion);
        Assert.Equal(6, result.Status.ToVersion);
        Assert.Equal(10, result.Config!.Automata.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.Mapped, diagnostic.Kind);
        Assert.True(file.TryGetPersisted(
            "AutoConcept",
            "FallbackEvaluationIntervalSeconds",
            out var persisted));
        Assert.Equal("10", persisted);
    }

    [Fact]
    public void SchemaSixMapsExplicitlySavedThreeHundredSecondFallbackToTenSeconds()
    {
        // The file format has no provenance; a deliberately saved 300 is serialized exactly like
        // the former default and therefore follows the same documented schema-6 mapping.
        var file = VersionFiveFile("300");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(10, result.Config!.Automata.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.Mapped, diagnostic.Kind);
        Assert.True(file.TryGetPersisted(
            "AutoConcept",
            "FallbackEvaluationIntervalSeconds",
            out var persisted));
        Assert.Equal("10", persisted);
    }

    [Fact]
    public void SchemaSixPreservesCustomizedAutoConceptFallback()
    {
        var file = VersionFiveFile("60");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(60, result.Config!.Automata.AutoConceptFallbackEvaluationIntervalSeconds.Value);
        Assert.Empty(result.Diagnostics);
    }

    private static ConfigFile VersionFiveFile(string fallbackSeconds)
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "5");
        file.SeedSerialized(
            "AutoConcept",
            "FallbackEvaluationIntervalSeconds",
            fallbackSeconds);
        return file;
    }
}
