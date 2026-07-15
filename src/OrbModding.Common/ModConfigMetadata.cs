namespace OrbModding.Common;

/// <summary>
/// Optional presentation metadata consumed by Orb Mod Config. BepInEx ignores
/// unknown ConfigDescription tags, so plugins remain usable without the UI mod.
/// </summary>
public sealed class ModConfigMetadata
{
    public ModConfigMetadata(
        int sectionOrder,
        int settingOrder,
        bool hidden = false,
        string? displaySection = null,
        string? displayName = null,
        bool restartRequired = false,
        string? dependencySection = null,
        string? dependencyKey = null,
        string dependencyValue = "true")
    {
        SectionOrder = sectionOrder;
        SettingOrder = settingOrder;
        Hidden = hidden;
        DisplaySection = displaySection;
        DisplayName = displayName;
        RestartRequired = restartRequired;
        DependencySection = dependencySection;
        DependencyKey = dependencyKey;
        DependencyValue = dependencyValue;
    }

    public int SectionOrder { get; }

    public int SettingOrder { get; }

    public bool Hidden { get; }
    public string? DisplaySection { get; }
    public string? DisplayName { get; }
    public bool RestartRequired { get; }
    public string? DependencySection { get; }
    public string? DependencyKey { get; }
    public string DependencyValue { get; }
}
