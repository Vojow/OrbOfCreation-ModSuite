namespace OrbModding.Common;

/// <summary>
/// Optional presentation metadata consumed by Orb Mod Config. BepInEx ignores
/// unknown ConfigDescription tags, so plugins remain usable without the UI mod.
/// </summary>
public sealed class ModConfigMetadata
{
    public ModConfigMetadata(int sectionOrder, int settingOrder, bool hidden = false)
    {
        SectionOrder = sectionOrder;
        SettingOrder = settingOrder;
        Hidden = hidden;
    }

    public int SectionOrder { get; }

    public int SettingOrder { get; }

    public bool Hidden { get; }
}
