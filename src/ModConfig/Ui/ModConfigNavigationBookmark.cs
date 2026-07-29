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
        var pages = ModConfigTopNavigation.Build(catalog, attentionCount: 0);
        for (var index = 1; index < pages.Count; index++)
        {
            var page = pages[index];
            var mod = catalog.Mods[page.PluginIndex];
            if (string.Equals(mod.Guid, bookmark.PluginGuid, StringComparison.Ordinal) &&
                (page.SectionIndex < 0 && string.IsNullOrEmpty(bookmark.SectionName) ||
                 page.SectionIndex >= 0 && string.Equals(
                     mod.Sections[page.SectionIndex].Name,
                     bookmark.SectionName,
                     StringComparison.Ordinal)))
                return index;
        }
        return 0;
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
