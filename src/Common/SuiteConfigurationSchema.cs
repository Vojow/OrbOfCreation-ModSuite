using System;

namespace OrbModding.Common;

/// <summary>
/// One assembly, one plugin GUID, one BepInEx configuration file — and therefore exactly one schema
/// plan. <see cref="ConfigurationSchemaTransaction"/> stores a single
/// <c>[OrbModding] ConfigurationSchemaVersion</c> marker per file, so three plans binding into one
/// file would fight over one number: the first writes its version and every later plan reads a
/// version it does not recognise. There is nothing to migrate from — the retired per-plugin files
/// carry different names and are never read — so version 1 is the first version this file ever had.
/// </summary>
internal static class SuiteConfigurationSchema
{
    internal const int CurrentVersion = 2;

    internal static ConfigurationSchemaPlan Plan { get; } = new(CurrentVersion, new[]
    {
        new ConfigurationMigrationStep(
            0,
            1,
            Array.Empty<ConfigurationKey>(),
            static _ => { }),
        // Nothing in the file changes shape here. The version moves so that one launch — the launch
        // that reads a file written before the differential verification chord moved off Mentor's
        // toggle key — can tell that a persisted shortcut is an inherited default rather than a
        // choice, and rebind it. Values are left where they are: the shortcut is bound outside this
        // transaction, and a migration step may only write keys the transaction itself binds.
        new ConfigurationMigrationStep(
            1,
            CurrentVersion,
            Array.Empty<ConfigurationKey>(),
            static _ => { }),
    });
}
