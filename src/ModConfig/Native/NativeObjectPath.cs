using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OrbModConfig;

/// <summary>
/// Formats diagnostic Unity hierarchy paths without reflecting over game types.
/// This boundary is used only during native navigation discovery and setup.
/// </summary>
internal static class NativeObjectPath
{
    public static string Build(UnityEngine.Object instance)
    {
        if (instance is null) return string.Empty;

        var transform = instance switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null,
        };
        if (transform is null) return instance.name ?? instance.GetType().Name;

        var segments = new List<string>(8);
        for (var current = transform; current is not null && segments.Count < 64; current = current.parent)
        {
            if (!string.IsNullOrWhiteSpace(current.name)) segments.Add(current.name);
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>
    /// Builds a closed-world selector that remains unique when Unity has several identically named
    /// clone rows. The sibling index is native hierarchy state, not a caller supplied reflection
    /// token, and is stable for the lifetime of the currently published screen catalog.
    /// </summary>
    public static string BuildIndexed(UnityEngine.Object instance)
    {
        if (instance is null) return string.Empty;

        var transform = instance switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null,
        };
        if (transform is null) return instance.name ?? instance.GetType().Name;

        var segments = new List<string>(8);
        for (var current = transform; current is not null && segments.Count < 64; current = current.parent)
        {
            if (string.IsNullOrWhiteSpace(current.name)) continue;
            segments.Add(
                current.name + "[" +
                current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture) + "]");
        }

        segments.Reverse();
        return string.Join("/", segments);
    }
}
