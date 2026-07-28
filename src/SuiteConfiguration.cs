using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;

namespace OrbModding;

/// <summary>
/// Everything the suite binds out of the one BepInEx configuration file, in one transaction.
/// <para>
/// The order matters where two features want the same <c>(section, key)</c>:
/// <see cref="ConfigFile.Bind{T}(ConfigDefinition,T,ConfigDescription)"/> returns the entry that is
/// already bound, so the first binder's description, ordering metadata and default win and later
/// binders simply receive the same entry. <c>General/Enabled</c> and
/// <c>Safety/EmergencyDisable</c> are shared that way on purpose: one master switch, one
/// emergency stop.
/// </para>
/// </summary>
internal sealed class SuiteConfiguration
{
    private SuiteConfiguration(
        BepInExAutomataConfiguration automata,
        MentorConfig mentor,
        ModConfigSettings modConfig)
    {
        Automata = automata;
        Mentor = mentor;
        ModConfig = modConfig;
    }

    internal BepInExAutomataConfiguration Automata { get; }

    internal MentorConfig Mentor { get; }

    internal ModConfigSettings ModConfig { get; }

    internal static ConfigurationSchemaBindResult<SuiteConfiguration> TryBind(
        ConfigFile file,
        IConfigurationFileOperations? fileOperations = null,
        ConfigurationSchemaStatusRegistry? statuses = null) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            file,
            SuiteConfigurationSchema.Plan,
            BindCurrent,
            fileOperations,
            statuses);

    private static SuiteConfiguration BindCurrent(ConfigFile file)
    {
        var configuration = new SuiteConfiguration(
            BepInExAutomataConfiguration.BindCurrent(file),
            MentorConfig.BindCurrent(file),
            ModConfigSettings.BindCurrent(file));
        file.Bind(
            SuiteConfigurationSchema.DifferentialVerificationShortcut.Section,
            SuiteConfigurationSchema.DifferentialVerificationShortcut.Key,
            new KeyboardShortcut(UnityEngine.KeyCode.None),
            new ConfigDescription(
                "Retained only to preserve a player-customized legacy value. The verifier is run from Mods > Runtime.",
                null,
                new ModConfigMetadata(int.MaxValue, int.MaxValue, hidden: true)));
        return configuration;
    }
}
