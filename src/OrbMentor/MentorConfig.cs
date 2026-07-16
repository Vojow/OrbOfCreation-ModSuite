using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbMentor;

internal enum MentorOperationMode { Disabled, Active }

internal sealed class MentorConfig
{
    private MentorConfig(ConfigEntry<bool> enabled, ConfigEntry<MentorOperationMode> mode,
        ConfigEntry<KeyboardShortcut> shortcut, ConfigEntry<bool> emergencyDisable,
        ConfigEntry<MentorEconomyMode> economyMode, ConfigEntry<double> sharePercent,
        ConfigEntry<bool> artifactsEnabled, ConfigEntry<double> artifactSharePercent,
        ConfigEntry<bool> alchemyEnabled, ConfigEntry<double> alchemySharePercent,
        ConfigEntry<int> operationsPerFrame, ConfigEntry<double> cpuBudgetMilliseconds,
        ConfigEntry<bool> detailedLogging, ConfigEntry<bool>? developmentProbe)
    {
        Enabled = enabled; Mode = mode; ToggleShortcut = shortcut; EmergencyDisable = emergencyDisable;
        EconomyMode = economyMode; SharePercent = sharePercent; OperationsPerFrame = operationsPerFrame;
        ArtifactsEnabled = artifactsEnabled; ArtifactSharePercent = artifactSharePercent;
        AlchemyEnabled = alchemyEnabled; AlchemySharePercent = alchemySharePercent;
        CpuBudgetMilliseconds = cpuBudgetMilliseconds; DetailedLogging = detailedLogging; DevelopmentProbe = developmentProbe;
    }

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<MentorOperationMode> Mode { get; }
    public ConfigEntry<KeyboardShortcut> ToggleShortcut { get; }
    public ConfigEntry<bool> EmergencyDisable { get; }
    public ConfigEntry<MentorEconomyMode> EconomyMode { get; }
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

    public static MentorConfig Bind(ConfigFile file) => new(
        Bind(file, "General", "Enabled", true, "Enable Orb Mentor. Mode still starts Disabled on fresh installations.", 0, 0, hidden: true),
        Bind(file, "General", "Mode", MentorOperationMode.Disabled, "Disabled rejects and clears sharing work. Active grants through native spell mastery.", 0, 0, displaySection: "Spells", displayName: "Spell sharing"),
        Bind(file, "General", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftAlt), "Toggle Disabled/Active. Default: Left Alt + M.", 0, 20, displaySection: "Spells", displayName: "Toggle shortcut"),
        Bind(file, "Safety", "EmergencyDisable", false, "Immediately reject new events and discard pending bonus work.", 30, 20, displaySection: "Advanced", displayName: "Emergency disable"),
        Bind(file, "Sharing", "EconomyMode", MentorEconomyMode.SharedPool, "SharedPool bounds total bonus XP. PerRecipient grants the percentage to every recipient and scales with collection size.", 30, 10, displaySection: "Advanced", displayName: "Economy mode"),
        Bind(file, "Sharing", "SharePercent", 10.0, "Final mentor XP percentage, clamped to 0-100.", 0, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Spells", displayName: "Spell share percent", dependencySection: "General", dependencyKey: "Mode", dependencyValue: "Active"),
        Bind(file, "Artifacts", "Enabled", false, "Share mastery XP earned by equipped artifacts with lower-mastery created artifacts.", 10, 0, displaySection: "Artifacts", displayName: "Artifact sharing"),
        Bind(file, "Artifacts", "SharePercent", 10.0, "Artifact mastery XP percentage, clamped to 0-100.", 10, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Artifacts", displayName: "Artifact share percent", dependencySection: "Artifacts", dependencyKey: "Enabled"),
        Bind(file, "Alchemy", "Enabled", false, "Share alchemy mastery XP with lower-mastery discovered recipes.", 20, 0, displaySection: "Alchemy", displayName: "Alchemy sharing"),
        Bind(file, "Alchemy", "SharePercent", 10.0, "Alchemy mastery XP percentage, clamped to 0-100.", 20, 10, new AcceptableValueRange<double>(0, 100), displaySection: "Alchemy", displayName: "Alchemy share percent", dependencySection: "Alchemy", dependencyKey: "Enabled"),
        Bind(file, "Performance", "OperationsPerFrame", 2, "Maximum successful native recipient grants per frame. Capture qualification and plan expansion use a separate bounded work limit and the same CPU budget.", 30, 30, new AcceptableValueRange<int>(1, 8), displaySection: "Advanced", displayName: "Operations per frame"),
        Bind(file, "Performance", "CpuBudgetMilliseconds", 0.5, "Soft unscaled CPU-time budget per frame, capped at 1 ms.", 30, 40, new AcceptableValueRange<double>(0.1, 1.0), displaySection: "Advanced", displayName: "CPU budget (ms)"),
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
        string? displaySection = null, string? displayName = null, string? dependencySection = null,
        string? dependencyKey = null, string dependencyValue = "true") =>
        file.Bind(section, key, value, new ConfigDescription(description, range,
            new ModConfigMetadata(sectionOrder, settingOrder, hidden, displaySection, displayName,
                dependencySection: dependencySection, dependencyKey: dependencyKey, dependencyValue: dependencyValue)));
}
