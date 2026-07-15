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
        Bind(file, "General", "Enabled", true, "Enable Orb Mentor. Mode still starts Disabled on fresh installations.", 0, 0),
        Bind(file, "General", "Mode", MentorOperationMode.Disabled, "Disabled rejects and clears sharing work. Active grants through native spell mastery.", 0, 10),
        Bind(file, "General", "ToggleShortcut", new KeyboardShortcut(UnityEngine.KeyCode.M, UnityEngine.KeyCode.LeftAlt), "Toggle Disabled/Active. Default: Left Alt + M.", 0, 20),
        Bind(file, "Safety", "EmergencyDisable", false, "Immediately reject new events and discard pending bonus work.", 30, 0),
        Bind(file, "Sharing", "EconomyMode", MentorEconomyMode.SharedPool, "SharedPool bounds total bonus XP. PerRecipient grants the percentage to every recipient and scales with collection size.", 10, 0),
        Bind(file, "Sharing", "SharePercent", 10.0, "Final mentor XP percentage, clamped to 0-100.", 10, 10, new AcceptableValueRange<double>(0, 100)),
        Bind(file, "Artifacts", "Enabled", false, "Share mastery XP earned by equipped artifacts with lower-mastery created artifacts.", 11, 0),
        Bind(file, "Artifacts", "SharePercent", 10.0, "Artifact mastery XP percentage, clamped to 0-100.", 11, 10, new AcceptableValueRange<double>(0, 100)),
        Bind(file, "Alchemy", "Enabled", false, "Share alchemy mastery XP with lower-mastery discovered recipes.", 12, 0),
        Bind(file, "Alchemy", "SharePercent", 10.0, "Alchemy mastery XP percentage, clamped to 0-100.", 12, 10, new AcceptableValueRange<double>(0, 100)),
        Bind(file, "Performance", "OperationsPerFrame", 8, "Maximum native recipient grants per frame.", 20, 0, new AcceptableValueRange<int>(1, 128)),
        Bind(file, "Performance", "CpuBudgetMilliseconds", 2.0, "Soft unscaled CPU-time budget per frame.", 20, 10, new AcceptableValueRange<double>(0.1, 10)),
        Bind(file, "Diagnostics", "DetailedLogging", false, "Log mentor events, batches, recipients, and amounts.", 40, 0),
        BindDevelopmentProbe(file));

    private static ConfigEntry<bool>? BindDevelopmentProbe(ConfigFile file)
    {
#if DEBUG
        return Bind(file, "Development", "EventProbe", false, "Log native mastery events for development validation.", 100, 0);
#else
        return null;
#endif
    }

    private static ConfigEntry<T> Bind<T>(ConfigFile file, string section, string key, T value, string description,
        int sectionOrder, int settingOrder, AcceptableValueBase? range = null) =>
        file.Bind(section, key, value, new ConfigDescription(description, range, new ModConfigMetadata(sectionOrder, settingOrder)));
}
