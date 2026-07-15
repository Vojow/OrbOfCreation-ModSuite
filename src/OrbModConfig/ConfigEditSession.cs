using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;

namespace OrbModConfig;

internal sealed class ConfigEditValue
{
    public ConfigEditValue(ConfigSettingDescriptor setting)
    {
        Setting = setting;
        OriginalSerialized = setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
    }

    public ConfigSettingDescriptor Setting { get; }
    public string OriginalSerialized { get; private set; }
    public string StagedSerialized { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public bool IsDirty => !string.Equals(OriginalSerialized, StagedSerialized, StringComparison.Ordinal);
    public bool IsValid => Error.Length == 0;
    public bool IsEditable => Setting.Kind != ConfigEditorKind.Unsupported;

    public void Stage(string serialized)
    {
        if (!IsEditable)
        {
            Error = "This setting type is read-only.";
            return;
        }

        StagedSerialized = serialized;
        Error = ConfigValueValidator.Validate(Setting, serialized);
    }

    public void StageDefault()
    {
        Stage(Setting.DefaultSerializedValue);
    }

    public void Revert()
    {
        OriginalSerialized = Setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
        Error = string.Empty;
    }

    public void AcceptAppliedValue()
    {
        OriginalSerialized = Setting.Source.GetSerializedValue();
        StagedSerialized = OriginalSerialized;
        Error = string.Empty;
    }
}

internal sealed class ConfigEditSession
{
    private readonly Dictionary<ConfigEntryBase, ConfigEditValue> _values;

    public ConfigEditSession(ConfigCatalogSnapshot catalog)
    {
        _values = catalog.Mods
            .SelectMany(mod => mod.Sections)
            .SelectMany(section => section.Settings)
            .ToDictionary(setting => setting.Source, setting => new ConfigEditValue(setting));
    }

    public IEnumerable<ConfigEditValue> Values => _values.Values;
    public bool IsDirty => _values.Values.Any(value => value.IsDirty);
    public bool IsValid => _values.Values.All(value => value.IsValid);

    public ConfigEditValue Get(ConfigSettingDescriptor setting) => _values[setting.Source];

    public bool DependencySatisfied(ConfigSettingDescriptor setting)
    {
        if (string.IsNullOrWhiteSpace(setting.DependencySection) || string.IsNullOrWhiteSpace(setting.DependencyKey)) return true;
        var dependency = _values.Values.FirstOrDefault(value =>
            ReferenceEquals(value.Setting.Source.ConfigFile, setting.Source.ConfigFile) &&
            string.Equals(value.Setting.SourceSection, setting.DependencySection, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.Setting.Key, setting.DependencyKey, StringComparison.OrdinalIgnoreCase));
        return dependency is not null && string.Equals(dependency.StagedSerialized, setting.DependencyValue, StringComparison.OrdinalIgnoreCase);
    }

    public void RevertAll()
    {
        foreach (var value in _values.Values)
        {
            value.Revert();
        }
    }

    public bool Apply(out string error)
    {
        error = string.Empty;
        var invalid = _values.Values.FirstOrDefault(value => !value.IsValid);
        if (invalid is not null)
        {
            error = $"{invalid.Setting.Section}.{invalid.Setting.Key}: {invalid.Error}";
            return false;
        }

        var dirty = _values.Values.Where(value => value.IsDirty).ToArray();
        if (dirty.Length == 0)
        {
            return true;
        }

        var originals = dirty.ToDictionary(value => value, value => value.Setting.Source.GetSerializedValue());
        try
        {
            foreach (var value in dirty)
            {
                value.Setting.Source.SetSerializedValue(value.StagedSerialized);
            }

            foreach (var configFile in dirty.Select(value => value.Setting.Source.ConfigFile).Distinct())
            {
                configFile.Save();
            }

            foreach (var value in dirty)
            {
                value.AcceptAppliedValue();
            }

            return true;
        }
        catch (Exception ex)
        {
            foreach (var pair in originals)
            {
                try
                {
                    pair.Key.Setting.Source.SetSerializedValue(pair.Value);
                }
                catch
                {
                }
            }

            foreach (var configFile in dirty.Select(value => value.Setting.Source.ConfigFile).Distinct())
            {
                try
                {
                    configFile.Save();
                }
                catch
                {
                }
            }

            error = ex.GetBaseException().Message;
            return false;
        }
    }
}

internal static class ConfigValueValidator
{
    public static string Validate(ConfigSettingDescriptor setting, string serialized)
    {
        if (setting.Kind == ConfigEditorKind.Unsupported)
        {
            return "This setting type is read-only.";
        }

        if (setting.Kind == ConfigEditorKind.String || setting.Kind == ConfigEditorKind.KeyboardShortcut)
        {
            return string.Empty;
        }

        if (!TryParse(setting.SettingType, serialized, out var parsed))
        {
            return $"Expected {FriendlyTypeName(setting.SettingType)}.";
        }

        var acceptable = setting.Source.Description.AcceptableValues;
        if (acceptable is not null)
        {
            var isValid = acceptable.GetType().GetMethod(
                "IsValid",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(object) },
                null);
            try
            {
                if (isValid?.Invoke(acceptable, new[] { parsed }) is false)
                {
                    return acceptable.ToDescriptionString();
                }
            }
            catch
            {
                return acceptable.ToDescriptionString();
            }
        }

        return string.Empty;
    }

    private static bool TryParse(Type type, string serialized, out object? value)
    {
        value = null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (type == typeof(bool))
            {
                if (!bool.TryParse(serialized, out var result))
                {
                    return false;
                }

                value = result;
                return true;
            }

            if (type.IsEnum)
            {
                value = Enum.Parse(type, serialized, true);
                return true;
            }

            value = Convert.ChangeType(serialized, type, CultureInfo.InvariantCulture);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string FriendlyTypeName(Type type)
    {
        return type.IsEnum ? string.Join(", ", Enum.GetNames(type)) : type.Name;
    }
}
