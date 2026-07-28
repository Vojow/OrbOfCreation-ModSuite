using System;
using System.Linq;

namespace OrbModConfig;

internal readonly record struct ModConfigNavigationBookmark(
    string PluginGuid,
    string SectionName,
    float ScrollOffset)
{
    public bool IsRuntime => string.IsNullOrEmpty(PluginGuid);

    public static ModConfigNavigationBookmark Runtime => new(string.Empty, string.Empty, 0f);
}

internal static class ModConfigNavigationBookmarkPolicy
{
    public static int ResolveTopPageIndex(
        ConfigCatalogSnapshot catalog,
        ModConfigNavigationBookmark bookmark)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (bookmark.IsRuntime) return 0;
        var pluginIndex = Array.FindIndex(
            catalog.Mods.ToArray(),
            mod => string.Equals(mod.Guid, bookmark.PluginGuid, StringComparison.Ordinal));
        return pluginIndex < 0 ? 0 : pluginIndex + 1;
    }

    public static int ResolveSectionIndex(
        ModConfigDescriptor mod,
        ModConfigNavigationBookmark bookmark)
    {
        if (mod is null) throw new ArgumentNullException(nameof(mod));
        var sectionIndex = Array.FindIndex(
            mod.Sections.ToArray(),
            section => string.Equals(section.Name, bookmark.SectionName, StringComparison.Ordinal));
        return Math.Max(0, sectionIndex);
    }
}
