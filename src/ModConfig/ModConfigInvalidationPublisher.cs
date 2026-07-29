using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;

namespace OrbModConfig;

internal static class ModConfigInvalidationPublisher
{
    public static int PublishAppliedSettings(
        GameplayInvalidationBus bus,
        long burst,
        IReadOnlyList<ConfigSettingDescriptor> appliedSettings)
    {
        if (bus is null) throw new ArgumentNullException(nameof(bus));
        if (appliedSettings is null) throw new ArgumentNullException(nameof(appliedSettings));

        var published = 0;
        for (var index = 0; index < appliedSettings.Count; index++)
        {
            var setting = appliedSettings[index];
            if (bus.Publish(
                    GameplayInvalidationKind.Configuration,
                    burst,
                    GameplayInvalidationDomains.ModConfig,
                    CreateEntityId(setting.PluginGuid, setting.SourceSection, setting.Key),
                    source: PluginIds.SuiteGuid))
            {
                published++;
            }
        }

        return published;
    }

    internal static string CreateEntityId(string pluginGuid, string section, string key)
    {
        if (pluginGuid is null) throw new ArgumentNullException(nameof(pluginGuid));
        if (section is null) throw new ArgumentNullException(nameof(section));
        if (key is null) throw new ArgumentNullException(nameof(key));

        // Length prefixes keep arbitrary plugin/config identifiers unambiguous
        // without exposing the owning ConfigFile path or presentation labels.
        return string.Concat(
            pluginGuid.Length.ToString(CultureInfo.InvariantCulture), ":", pluginGuid,
            section.Length.ToString(CultureInfo.InvariantCulture), ":", section,
            key.Length.ToString(CultureInfo.InvariantCulture), ":", key);
    }
}
