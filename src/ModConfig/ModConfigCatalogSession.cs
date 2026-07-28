using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace OrbModConfig;

internal readonly record struct ConfigCatalogGeneration(string Signature)
{
    public static ConfigCatalogGeneration Capture(IEnumerable<ConfigPluginSource> sources)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        var signature = new StringBuilder();
        foreach (var source in sources.OrderBy(source => source.Guid, StringComparer.Ordinal))
        {
            signature
                .Append(source.Guid).Append('\u001f')
                .Append(source.Version).Append('\u001f')
                .Append(RuntimeHelpers.GetHashCode(source.Config)).Append('\u001e');
            foreach (var entry in source.Config
                         .OrderBy(pair => pair.Key.Section, StringComparer.Ordinal)
                         .ThenBy(pair => pair.Key.Key, StringComparer.Ordinal))
            {
                signature
                    .Append(entry.Key.Section).Append('\u001f')
                    .Append(entry.Key.Key).Append('\u001f')
                    .Append(entry.Value.SettingType.AssemblyQualifiedName).Append('\u001e');
            }
            signature.Append('\u001d');
        }
        return new ConfigCatalogGeneration(signature.ToString());
    }
}

internal static class ModConfigCatalogSession
{
    public static ConfigCatalogSnapshot GetOrDiscover(
        ref ConfigCatalogSnapshot? catalog,
        ref ConfigCatalogGeneration generation,
        ConfigCatalogGeneration currentGeneration,
        Func<ConfigCatalogSnapshot> discover,
        Action<ConfigCatalogSnapshot> logDiscovered)
    {
        if (catalog is not null && generation == currentGeneration) return catalog;
        catalog = discover();
        generation = currentGeneration;
        logDiscovered(catalog);
        return catalog;
    }

    public static bool IsCurrent(
        ConfigCatalogSnapshot? catalog,
        ConfigCatalogGeneration generation,
        ConfigCatalogGeneration currentGeneration) =>
        catalog is not null && generation == currentGeneration;
}
