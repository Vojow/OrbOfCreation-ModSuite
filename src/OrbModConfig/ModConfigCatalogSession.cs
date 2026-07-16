using System;

namespace OrbModConfig;

internal static class ModConfigCatalogSession
{
    public static ConfigCatalogSnapshot GetOrDiscover(
        ref ConfigCatalogSnapshot? catalog,
        Func<ConfigCatalogSnapshot> discover,
        Action<ConfigCatalogSnapshot> logDiscovered)
    {
        if (catalog is not null) return catalog;
        catalog = discover();
        logDiscovered(catalog);
        return catalog;
    }
}
