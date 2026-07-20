using System;
using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModConfigSettings
{
    private static readonly ConfigurationSchemaPlan Schema = new(1, new[]
    {
        new ConfigurationMigrationStep(0, 1, Array.Empty<ConfigurationKey>(), _ => { }),
    });

    private ModConfigSettings(ConfigEntry<bool> enabled, ConfigEntry<bool> enableUiShell)
    {
        Enabled = enabled;
        EnableUiShell = enableUiShell;
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<bool> EnableUiShell { get; }

    public static ConfigurationSchemaBindResult<ModConfigSettings> TryBind(ConfigFile file) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.ModConfigGuid,
            file,
            Schema,
            BindCurrent);

    private static ModConfigSettings BindCurrent(ConfigFile file) => new(
        file.Bind(
            "General",
            "Enabled",
            true,
            new ConfigDescription(
                "Enable Orb Mod Config.",
                null,
                new ModConfigMetadata(0, 0, hidden: true))),
        file.Bind(
            "Interface",
            "EnableButtonShell",
            true,
            new ConfigDescription(
                "Insert the Mods top-bar button and in-game configuration editor.",
                null,
                new ModConfigMetadata(10, 0, hidden: true))));
}
