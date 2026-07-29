using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbModConfig;

internal sealed class ModConfigSettings
{
    private ModConfigSettings(ConfigEntry<bool> enabled, ConfigEntry<bool> enableUiShell)
    {
        Enabled = enabled;
        EnableUiShell = enableUiShell;
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<bool> EnableUiShell { get; }

    public static ConfigurationSchemaBindResult<ModConfigSettings> TryBind(ConfigFile file) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            file,
            SuiteConfigurationSchema.Plan,
            BindCurrent);

    internal static ModConfigSettings BindCurrent(ConfigFile file) => new(
        file.Bind(
            "General",
            "Enabled",
            true,
            new ConfigDescription(
                "Master switch for the whole suite.",
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
