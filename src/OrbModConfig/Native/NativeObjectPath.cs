using System.Collections.Generic;
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
}
