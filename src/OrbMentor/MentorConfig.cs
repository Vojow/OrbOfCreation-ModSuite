using System.Collections.Generic;
using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbMentor;

internal enum MentorOperationMode { Disabled, Active }
internal enum MentorSpellSourcePolicy { EquippedSpells, HighestDiscovered }

internal sealed class MentorConfig
{
    private static readonly IReadOnlyList<ModConfigDependency> ActiveDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> ActiveArtifactDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
        new ModConfigDependency("Artifacts", "Enabled"),
    };
    private static readonly IReadOnlyList<ModConfigDependency> ActiveAlchemyDependencies = new[]
    {
        new ModConfigDependency("General", "Mode", "Active"),
        new ModConfigDependency("Alchemy", "Enabled"),
    };

    private MentorConfig(ConfigEntry<bool> enabled, ConfigEntry<MentorOperationMode> mode,
        ConfigEntry<KeyboardShortcut> shortcut, ConfigEntry<bool> emergencyDisable,
        ConfigEntry<MentorEconomyMode> economyMode, ConfigEntry<MentorSpellSourcePolicy> spellSourcePolicy,
        ConfigEntry<double> sharePercent,
        ConfigEntry<bool> artifactsEnabled, ConfigEntry<double> artifactSharePercent,
        ConfigEntry<bool> alchemyEnabled, ConfigEntry<double> alchemySharePercent,
        ConfigEntry<int> operationsPerFrame, ConfigEntry<double> cpuBudgetMilliseconds,
        ConfigEntry<bool> detailedLogging, ConfigEntry<bool>? developmentProbe)
    {
        Enabled = enabled; Mode = mode; ToggleShortcut = shortcut; EmergencyDisable = emergencyDisable;
        EconomyMode = economyMode; SharePercent = sharePercent; OperationsPerFrame = operationsPerFrame;
        SpellSourcePolicy = spellSourcePolicy;
        ArtifactsEnabled = artifactsEnabled; ArtifactSharePercent = artifactSharePercent;
        AlchemyEnabled = alchemyEnabled; AlchemySharePercent = alchemySharePercent;
        CpuBudgetMilliseconds = cpuBudgetMilliseconds; DetailedLogging = detailedLogging; DevelopmentProbe = developmentProbe;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<MentorOperationMode> Mode { get; }
    public ConfigEntry<KeyboardShortcut> ToggleShortcut { get; }
    public ConfigEntry<bool> EmergencyDisable { get; }
    public ConfigEntry<MentorEconomyMode> EconomyMode { get; }
    public ConfigEntry<MentorSpellSourcePolicy> SpellSourcePolicy { get; }
    public ConfigEntry<double> SharePercent { get; }
    public ConfigEntry<bool> ArtifactsEnabled { get; }
    public ConfigEntry<double> ArtifactSharePercent { get; }
    public ConfigEntry<bool> AlchemyEnabled { get; }
    public ConfigEntry<double> AlchemySharePercent { get; }
    public ConfigEntry<int> OperationsPerFrame { get; }
    public ConfigEntry<double> CpuBudgetMilliseconds { get; }
    public ConfigEntry<bool> DetailedLogging { get; }
    public ConfigEntry<bool>? DevelopmentProbe { get; }
    public bool DevelopmentProbeEnabled => DevelopmentProbe?.Value == true;
    public bool Active => Enabled.Value && Mode.Value == MentorOperationMode.Active && !EmergencyDisable.Value;

    public static MentorConfig Bind(ConfigFile file)
    {
        var result = TryBind(file);
        if (!result.Success) throw new System.InvalidOperationException(result.Status.Reason);
        return result.Config!;
    }

    public static ConfigurationSchemaBindResult<MentorConfig> TryBind(ConfigFile file) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.MentorGuid,
            file,
            MentorConfigurationSchema.Plan,
            BindCurrent);

    private static MentorConfig BindCurrent(ConfigFile file) => new(
        Bind(file, "General", "Enabled", true, "Enable Orb Mentor. Mode still starts Disabled on fresh installations.", 0, 0, hidden: true),
        Bind(file, "General", "Mode", MentorOperationMode.Disabled, "Disabled rejects and clears sharing work. Active grants through native mastery paths.", 0, 0, displaySection: "Spells", displayName: "Mentor"),
        Bind(file, "General", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftAlt), "Toggle Disabled/Active. Default: Left Alt + M.", 0, 20, displaySection: "Spells", displayName: "Toggle shortcut"),
        Bind(file, "Safety", "EmergencyDisable", false, "Immediately reject new events and discard pending bonus work.", 30, 20, displaySection: "Advanced", displayName: "Emergency disable"),
        Bind(file, "Sharing", "EconomyMode", MentorEconomyMode.SharedPool, "SharedPool bounds total bonus XP. PerRecipient grants the percentage to every recipient and scales with collection size.", 30, 10, displaySection: "Advanced", displayName: "Economy mode", dependencies: ActiveDependencies),
        Bind(file, "Sharing", "SpellSourcePolicy", MentorSpellSourcePolicy.EquippedSpells, "EquippedSpells lets every equipped spell share its native mastery XP with discovered spells below that source's mastery. HighestDiscovered keeps the original highest-mastery-only rule.", 0, 5, displaySection: "Spells", displayName: "Sharing sources", dependencies: ActiveDependencies),
        Bind(file, "Sharing", "SharePercent", 10.0, "Final mentor XP percentage, clamped to 0-100.", 0, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Spells", displayName: "Spell share percent", dependencies: ActiveDependencies),
        Bind(file, "Artifacts", "Enabled", false, "Share mastery XP earned by equipped artifacts with lower-mastery created artifacts.", 10, 0, displaySection: "Artifacts", displayName: "Artifact sharing", dependencies: ActiveDependencies),
        Bind(file, "Artifacts", "SharePercent", 10.0, "Artifact mastery XP percentage, clamped to 0-100.", 10, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Artifacts", displayName: "Artifact share percent", dependencies: ActiveArtifactDependencies),
        Bind(file, "Alchemy", "Enabled", false, "Share alchemy mastery XP with lower-mastery discovered recipes.", 20, 0, displaySection: "Alchemy", displayName: "Alchemy sharing", dependencies: ActiveDependencies),
        Bind(file, "Alchemy", "SharePercent", 10.0, "Alchemy mastery XP percentage, clamped to 0-100.", 20, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Alchemy", displayName: "Alchemy share percent", dependencies: ActiveAlchemyDependencies),
        Bind(file, "Performance", "OperationsPerFrame", 2, "Maximum successful native recipient grants per frame. Capture qualification and plan expansion use a separate bounded work limit and the same CPU budget.", 30, 30, new AcceptableValueRange<int>(1, 8), displaySection: "Advanced", displayName: "Operations per frame", dependencies: ActiveDependencies),
        Bind(file, "Performance", "CpuBudgetMilliseconds", 0.5, "Soft unscaled CPU-time budget per frame, capped at 1 ms.", 30, 40, new AcceptableValueRange<double>(0.1, 1.0), displaySection: "Advanced", displayName: "CPU budget (ms)", dependencies: ActiveDependencies),
        Bind(file, "Diagnostics", "DetailedLogging", false, "Log mentor events, batches, recipients, and amounts.", 30, 50, displaySection: "Advanced", displayName: "Detailed logging"),
        BindDevelopmentProbe(file));

    private static ConfigEntry<bool>? BindDevelopmentProbe(ConfigFile file)
    {
#if DEBUG
        return Bind(file, "Development", "EventProbe", false, "Log native mastery events for development validation.", 100, 0, hidden: true);
#else
        return null;
#endif
    }

    private static ConfigEntry<T> Bind<T>(ConfigFile file, string section, string key, T value, string description,
        int sectionOrder, int settingOrder, AcceptableValueBase? range = null, bool hidden = false,
        string? displaySection = null, string? displayName = null,
        IReadOnlyList<ModConfigDependency>? dependencies = null)
    {
        var metadata = dependencies is null
            ? new ModConfigMetadata(sectionOrder, settingOrder, hidden, displaySection, displayName)
            : new ModConfigMetadata(sectionOrder, settingOrder, dependencies, hidden, displaySection, displayName);
        return file.Bind(section, key, value, new ConfigDescription(description, range, metadata));
    }
}
