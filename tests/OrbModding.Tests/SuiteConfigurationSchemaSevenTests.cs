using BepInEx.Configuration;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class SuiteConfigurationSchemaSevenTests
{
    [Fact]
    public void SchemaSevenMapsSerializedThreeHundredSecondTrainingPeriodToThirtySeconds()
    {
        var file = VersionSixFile("300");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(6, result.Status.FromVersion);
        Assert.Equal(7, result.Status.ToVersion);
        Assert.Equal(30, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.Mapped, diagnostic.Kind);
        Assert.True(file.TryGetPersisted(
            "AutoConcept",
            "TrainingPeriodSeconds",
            out var persisted));
        Assert.Equal("30", persisted);
    }

    [Fact]
    public void SchemaSevenMapsExplicitlySavedThreeHundredSecondTrainingPeriodToThirtySeconds()
    {
        // The file format has no provenance; a deliberately saved 300 is serialized exactly like
        // the former default and therefore follows the same documented schema-7 mapping.
        var file = VersionSixFile("300");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(30, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ConfigurationMigrationDiagnosticKind.Mapped, diagnostic.Kind);
        Assert.True(file.TryGetPersisted(
            "AutoConcept",
            "TrainingPeriodSeconds",
            out var persisted));
        Assert.Equal("30", persisted);
    }

    [Fact]
    public void SchemaSevenPreservesCustomizedAutoConceptTrainingPeriod()
    {
        var file = VersionSixFile("60");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(60, result.Config!.Automata.AutoConceptTrainingPeriodSeconds.Value);
        Assert.Empty(result.Diagnostics);
    }

    private static ConfigFile VersionSixFile(string trainingPeriodSeconds)
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "6");
        file.SeedSerialized(
            "AutoConcept",
            "TrainingPeriodSeconds",
            trainingPeriodSeconds);
        return file;
    }
}
