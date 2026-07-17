using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using OrbModding.Common;

namespace OrbModConfig;

internal enum ConfigEditorKind
{
    Boolean,
    Enum,
    BoundedNumeric,
    Numeric,
    String,
    KeyboardShortcut,
    Unsupported,
}

internal sealed class ConfigPluginSource
{
    public ConfigPluginSource(string guid, string name, string version, ConfigFile config)
    {
        Guid = guid;
        Name = name;
        Version = version;
        Config = config;
    }

    public string Guid { get; }
    public string Name { get; }
    public string Version { get; }
    public ConfigFile Config { get; }
}

internal sealed class ConfigCatalogSnapshot
{
    public ConfigCatalogSnapshot(IReadOnlyList<ModConfigDescriptor> mods)
    {
        Mods = mods;
        SettingCount = mods.Sum(mod => mod.Sections.Sum(section => section.Settings.Count));
    }

    public IReadOnlyList<ModConfigDescriptor> Mods { get; }
    public int SettingCount { get; }
}

internal sealed class ModConfigDescriptor
{
    public ModConfigDescriptor(string guid, string name, string version, IReadOnlyList<ConfigSectionDescriptor> sections)
    {
        Guid = guid;
        Name = name;
        Version = version;
        Sections = sections;
    }

    public string Guid { get; }
    public string Name { get; }
    public string Version { get; }
    public IReadOnlyList<ConfigSectionDescriptor> Sections { get; }
}

internal sealed class ConfigSectionDescriptor
{
    public ConfigSectionDescriptor(string name, IReadOnlyList<ConfigSettingDescriptor> settings)
    {
        Name = name;
        Settings = settings;
    }

    public string Name { get; }
    public IReadOnlyList<ConfigSettingDescriptor> Settings { get; }
}

internal sealed class ConfigSettingDescriptor
{
    public ConfigSettingDescriptor(ConfigEntryBase source)
    {
        Source = source;
        SourceSection = source.Definition.Section;
        Key = source.Definition.Key;
        Description = source.Description.Description ?? string.Empty;
        SettingType = source.SettingType;
        Kind = ConfigCatalog.Classify(source);
        CurrentSerializedValue = source.GetSerializedValue();
        DefaultSerializedValue = ConfigCatalog.Serialize(source.DefaultValue);
        AcceptableValuesDescription = source.Description.AcceptableValues?.ToDescriptionString() ?? string.Empty;
        var metadata = source.Description.Tags.OfType<ModConfigMetadata>().FirstOrDefault();
        Section = metadata?.DisplaySection ?? SourceSection;
        DisplayName = metadata?.DisplayName ?? Humanize(Key);
        SectionOrder = metadata?.SectionOrder ?? int.MaxValue;
        SettingOrder = metadata?.SettingOrder ?? int.MaxValue;
        Hidden = metadata?.Hidden == true;
        RestartRequired = metadata?.RestartRequired == true;
        DependencySection = metadata?.DependencySection;
        DependencyKey = metadata?.DependencyKey;
        DependencyValue = metadata?.DependencyValue ?? "true";
        Dependencies = metadata?.Dependencies ?? Array.Empty<ModConfigDependency>();
    }

    public ConfigEntryBase Source { get; }
    public string SourceSection { get; }
    public string Section { get; }
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public Type SettingType { get; }
    public ConfigEditorKind Kind { get; }
    public string CurrentSerializedValue { get; }
    public string DefaultSerializedValue { get; }
    public string AcceptableValuesDescription { get; }
    public int SectionOrder { get; }
    public int SettingOrder { get; }
    public bool Hidden { get; }
    public bool RestartRequired { get; }
    public string? DependencySection { get; }
    public string? DependencyKey { get; }
    public string DependencyValue { get; }
    public IReadOnlyList<ModConfigDependency> Dependencies { get; }

    private static string Humanize(string value)
    {
        var chars = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1])) chars.Add(' ');
            chars.Add(value[index]);
        }
        return new string(chars.ToArray());
    }
}

internal static class ConfigCatalog
{
    private static readonly HashSet<Type> NumericTypes = new HashSet<Type>
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    };

    public static ConfigCatalogSnapshot DiscoverLoaded()
    {
        var sources = Chainloader.PluginInfos.Values
            .Where(plugin => plugin.Instance is not null)
            .Select(plugin => new ConfigPluginSource(
                plugin.Metadata.GUID,
                plugin.Metadata.Name,
                plugin.Metadata.Version.ToString(),
                plugin.Instance!.Config));

        return Build(sources);
    }

    public static ConfigCatalogSnapshot Build(IEnumerable<ConfigPluginSource> sources)
    {
        var mods = sources
            .Select(BuildMod)
            .Where(mod => mod.Sections.Count > 0)
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.Guid, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ConfigCatalogSnapshot(mods);
    }

    internal static ConfigEditorKind Classify(ConfigEntryBase entry)
    {
        var type = Nullable.GetUnderlyingType(entry.SettingType) ?? entry.SettingType;
        if (type == typeof(bool))
        {
            return ConfigEditorKind.Boolean;
        }

        if (type.IsEnum)
        {
            return ConfigEditorKind.Enum;
        }

        if (NumericTypes.Contains(type))
        {
            return IsRange(entry.Description.AcceptableValues)
                ? ConfigEditorKind.BoundedNumeric
                : ConfigEditorKind.Numeric;
        }

        if (type == typeof(string))
        {
            return ConfigEditorKind.String;
        }

        if (type == typeof(KeyboardShortcut))
        {
            return ConfigEditorKind.KeyboardShortcut;
        }

        return ConfigEditorKind.Unsupported;
    }

    internal static string Serialize(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static ModConfigDescriptor BuildMod(ConfigPluginSource source)
    {
        var settings = source.Config
            .Select(pair => new ConfigSettingDescriptor(pair.Value))
            .Where(setting => !setting.Hidden);

        var sections = settings
            .GroupBy(setting => setting.Section, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(setting => setting.SectionOrder))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ConfigSectionDescriptor(
                group.Key,
                group.OrderBy(setting => setting.SettingOrder)
                    .ThenBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        return new ModConfigDescriptor(source.Guid, source.Name, source.Version, sections);
    }

    private static bool IsRange(AcceptableValueBase? acceptableValues)
    {
        var type = acceptableValues?.GetType();
        return type is not null &&
               type.IsGenericType &&
               type.GetGenericTypeDefinition().Name == "AcceptableValueRange`1";
    }
}
