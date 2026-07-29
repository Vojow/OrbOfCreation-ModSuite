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
    private ModConfigTopPage(
        ModConfigTopPageKind kind,
        string label,
        int pluginIndex,
        int sectionIndex)
    {
        Kind = kind;
        Label = label;
        PluginIndex = pluginIndex;
        SectionIndex = sectionIndex;
    }

    public ModConfigTopPageKind Kind { get; }
    public string Label { get; }
    public int PluginIndex { get; }
    public int SectionIndex { get; }

    public static ModConfigTopPage Runtime(int attentionCount) => new(
        ModConfigTopPageKind.Runtime,
        attentionCount > 0 ? $"Runtime ({attentionCount})" : "Runtime",
        pluginIndex: -1,
        sectionIndex: -1);

    public static ModConfigTopPage Plugin(string label, int pluginIndex, int sectionIndex)
    {
        if (pluginIndex < 0) throw new ArgumentOutOfRangeException(nameof(pluginIndex));
        if (sectionIndex < -1) throw new ArgumentOutOfRangeException(nameof(sectionIndex));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A plugin page label is required.", nameof(label));
        return new ModConfigTopPage(
            ModConfigTopPageKind.PluginSettings,
            label,
            pluginIndex,
            sectionIndex);
    }
}

internal static class ModConfigTopNavigation
{
    public static string DetailTitle(ConfigCatalogSnapshot catalog, ModConfigTopPage page)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (page.Kind == ModConfigTopPageKind.Runtime) return "Orb Of Creation ModSuite · Runtime";
        var mod = catalog.Mods[page.PluginIndex];
        return page.SectionIndex < 0
            ? mod.Name
            : mod.Name + " · " + mod.Sections[page.SectionIndex].Name;
    }

    public static IReadOnlyList<ModConfigTopPage> Build(ConfigCatalogSnapshot catalog, int attentionCount)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (attentionCount < 0) throw new ArgumentOutOfRangeException(nameof(attentionCount));
        var pages = new List<ModConfigTopPage> { ModConfigTopPage.Runtime(attentionCount) };
        for (var index = 0; index < catalog.Mods.Count; index++)
        {
            var mod = catalog.Mods[index];
            if (mod.Sections.Count == 0)
            {
                pages.Add(ModConfigTopPage.Plugin(mod.Name, index, sectionIndex: -1));
                continue;
            }
            for (var sectionIndex = 0; sectionIndex < mod.Sections.Count; sectionIndex++)
            {
                var label = catalog.Mods.Count == 1
                    ? mod.Sections[sectionIndex].Name
                    : mod.Name + " · " + mod.Sections[sectionIndex].Name;
                pages.Add(ModConfigTopPage.Plugin(label, index, sectionIndex));
            }
        }
        return pages;
    }
}
