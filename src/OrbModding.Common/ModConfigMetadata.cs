using System;
using System.Collections.Generic;

namespace OrbModding.Common;

public sealed class ModConfigDependency
{
    public ModConfigDependency(string section, string key, string expectedValue = "true")
    {
        Section = section;
        Key = key;
        ExpectedValue = expectedValue;
    }

    public string Section { get; }
    public string Key { get; }
    public string ExpectedValue { get; }
}

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
        Dependencies = string.IsNullOrWhiteSpace(dependencySection) || string.IsNullOrWhiteSpace(dependencyKey)
            ? Array.Empty<ModConfigDependency>()
            : new[] { new ModConfigDependency(dependencySection!, dependencyKey!, dependencyValue) };
    }

    public ModConfigMetadata(
        int sectionOrder,
        int settingOrder,
        IReadOnlyList<ModConfigDependency> dependencies,
        bool hidden = false,
        string? displaySection = null,
        string? displayName = null,
        bool restartRequired = false)
    {
        SectionOrder = sectionOrder;
        SettingOrder = settingOrder;
        Hidden = hidden;
        DisplaySection = displaySection;
        DisplayName = displayName;
        RestartRequired = restartRequired;
        var copy = new ModConfigDependency[dependencies?.Count ?? 0];
        for (var index = 0; index < copy.Length; index++) copy[index] = dependencies![index];
        Dependencies = copy;
        DependencySection = copy.Length > 0 ? copy[0].Section : null;
        DependencyKey = copy.Length > 0 ? copy[0].Key : null;
        DependencyValue = copy.Length > 0 ? copy[0].ExpectedValue : "true";
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
    public IReadOnlyList<ModConfigDependency> Dependencies { get; }
}
