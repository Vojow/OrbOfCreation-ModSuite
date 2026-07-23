using System;
using System.Collections.Generic;

namespace OrbModConfig;

internal enum ModConfigTopPageKind
{
    Runtime = 0,
    PluginSettings = 1,
}

internal readonly struct ModConfigTopPage
{
    private ModConfigTopPage(ModConfigTopPageKind kind, string label, int pluginIndex)
    {
        Kind = kind;
        Label = label;
        PluginIndex = pluginIndex;
    }

    public ModConfigTopPageKind Kind { get; }
    public string Label { get; }
    public int PluginIndex { get; }

    public static ModConfigTopPage Runtime(int attentionCount) => new(
        ModConfigTopPageKind.Runtime,
        attentionCount > 0 ? $"Runtime ({attentionCount})" : "Runtime",
        pluginIndex: -1);

    public static ModConfigTopPage Plugin(string label, int pluginIndex)
    {
        if (pluginIndex < 0) throw new ArgumentOutOfRangeException(nameof(pluginIndex));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A plugin page label is required.", nameof(label));
        return new ModConfigTopPage(ModConfigTopPageKind.PluginSettings, label, pluginIndex);
    }
}

internal static class ModConfigTopNavigation
{
    public static IReadOnlyList<ModConfigTopPage> Build(ConfigCatalogSnapshot catalog, int attentionCount)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (attentionCount < 0) throw new ArgumentOutOfRangeException(nameof(attentionCount));
        var pages = new ModConfigTopPage[catalog.Mods.Count + 1];
        pages[0] = ModConfigTopPage.Runtime(attentionCount);
        for (var index = 0; index < catalog.Mods.Count; index++)
        {
            pages[index + 1] = ModConfigTopPage.Plugin(
                catalog.Mods[index].Name.Replace("Orb ", string.Empty),
                index);
        }
        return pages;
    }
}
