using BepInEx.Configuration;

namespace OrbAchievementResonance;

internal sealed class ResonanceConfig
{
    public ResonanceConfig(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Enable Achievement Resonance.");
        ApplyNativeEffectBlocks = config.Bind(
            "General",
            "ApplyNativeEffectBlocks",
            false,
            "Append native Achievement Strength persistent-effect blocks. Keep false until the read-only runtime probe passes on this game build.");
        StrengthDivisor = config.Bind(
            "Formula",
            "StrengthDivisor",
            1.0,
            "Divide Achievement Strength before applying per-strength rates. Values below 1 are clamped to 1.");

        Speed = BonusConfig.Create(config, "Speed", true, 0.0005, 1.25, "GlobalSpeedGroup.");
        Power = BonusConfig.Create(config, "Power", false, 0.0005, 1.25, "Agromancy, Alchemy, Manufacturing, and Mental power groups.");
        Duration = BonusConfig.Create(config, "Duration", false, 0.00025, 1.15, "AllDurationGroup. Off until group membership overlap is probed.");
        Special = BonusConfig.Create(config, "Special", false, 0.00025, 1.15, "AllSpecialsGroup. Off until group membership overlap is probed.");
        ResourceRate = BonusConfig.Create(config, "ResourceRate", false, 0.00025, 1.15, "GlobalResourceType.Rate only. GainRate is intentionally excluded.");
        ResourceCapacity = BonusConfig.Create(config, "ResourceCapacity", false, 0.00025, 1.15, "GlobalCappedResourceType.MaxQuantity.");
        Casting = BonusConfig.Create(config, "Casting", false, 0.00025, 1.15, "Spell power, special, duration, cast speed, and cooldown speed.");
        CastingProgression = BonusConfig.Create(config, "CastingProgression", false, 0.0001, 1.10, "Spell mastery and experience rate. Advanced and off by default.");

        RemoveExistingOwnedBlocksBeforeInject = config.Bind(
            "Runtime",
            "RemoveExistingOwnedBlocksBeforeInject",
            true,
            "Remove only Resonance-owned persistent-effect blocks before appending and binding the current configured set. Required for native injection.");
        CleanupOwnedBlocksOnDestroy = config.Bind(
            "Runtime",
            "CleanupOwnedBlocksOnDestroy",
            false,
            "Attempt to remove Resonance-owned persistent-effect blocks on plugin destroy. Active native modifiers are not broadly removed.");

        WarnOnAssemblyMismatch = config.Bind("Diagnostics", "WarnOnAssemblyMismatch", true, "Warn when installed game assemblies differ from the audited build.");
        LogCatalogOnStartup = config.Bind("Diagnostics", "LogCatalogOnStartup", true, "Log configured target descriptors on plugin startup.");
        LogSkippedTargets = config.Bind("Diagnostics", "LogSkippedTargets", true, "Log disabled or unresolved target descriptors during ManagerStart injection.");
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<bool> ApplyNativeEffectBlocks { get; }

    public ConfigEntry<double> StrengthDivisor { get; }

    public BonusConfig Speed { get; }

    public BonusConfig Power { get; }

    public BonusConfig Duration { get; }

    public BonusConfig Special { get; }

    public BonusConfig ResourceRate { get; }

    public BonusConfig ResourceCapacity { get; }

    public BonusConfig Casting { get; }

    public BonusConfig CastingProgression { get; }

    public ConfigEntry<bool> RemoveExistingOwnedBlocksBeforeInject { get; }

    public ConfigEntry<bool> CleanupOwnedBlocksOnDestroy { get; }

    public ConfigEntry<bool> WarnOnAssemblyMismatch { get; }

    public ConfigEntry<bool> LogCatalogOnStartup { get; }

    public ConfigEntry<bool> LogSkippedTargets { get; }

    public BonusConfig GetBonus(ResonanceBonusCategory category)
    {
        switch (category)
        {
            case ResonanceBonusCategory.Speed:
                return Speed;
            case ResonanceBonusCategory.Power:
                return Power;
            case ResonanceBonusCategory.Duration:
                return Duration;
            case ResonanceBonusCategory.Special:
                return Special;
            case ResonanceBonusCategory.ResourceRate:
                return ResourceRate;
            case ResonanceBonusCategory.ResourceCapacity:
                return ResourceCapacity;
            case ResonanceBonusCategory.Casting:
                return Casting;
            case ResonanceBonusCategory.CastingProgression:
                return CastingProgression;
            default:
                return Speed;
        }
    }
}

internal sealed class BonusConfig
{
    private BonusConfig(ConfigEntry<bool> enabled, ConfigEntry<double> perStrengthRate, ConfigEntry<double> maximumMultiplier)
    {
        Enabled = enabled;
        PerStrengthRate = perStrengthRate;
        MaximumMultiplier = maximumMultiplier;
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<double> PerStrengthRate { get; }

    public ConfigEntry<double> MaximumMultiplier { get; }

    public static BonusConfig Create(ConfigFile config, string section, bool enabled, double perStrengthRate, double maximumMultiplier, string details)
    {
        return new BonusConfig(
            config.Bind(section, "Enabled", enabled, $"Enable this Achievement Strength bonus. Target: {details}"),
            config.Bind(section, "PerStrengthRate", perStrengthRate, "Stacking modifier rate per effective Achievement Strength point."),
            config.Bind(section, "MaximumMultiplier", maximumMultiplier, "Maximum total multiplier for this category. Must be finite and greater than 1."));
    }
}
