using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace OrbModConfig;

internal sealed class NavigationProbeResult
{
    public NavigationProbeResult(int objectCount, IReadOnlyList<string> anchors)
    {
        ObjectCount = objectCount;
        Anchors = anchors;
    }

    public int ObjectCount { get; }
    public IReadOnlyList<string> Anchors { get; }
}

internal static class NavigationProbe
{
    private static readonly string[] TypeNames =
    {
        "CoreViewManager",
        "ViewManager",
        "UIViewRadio",
        "UIViewRadioButton",
        "ManagedView",
    };

    private static readonly string[] AnchorNames = { "Workshop", "Alchemy", "Time" };

    public static NavigationProbeResult Run(ManualLogSource log, int maxLoggedObjects)
    {
        var discovered = new List<string>();
        var anchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var typeName in TypeNames)
        {
            var type = Type.GetType(typeName + ", Assembly-CSharp", false);
            if (type is null)
            {
                log.LogWarning($"Mod Config UI probe could not resolve game type {typeName}.");
                continue;
            }

            var objects = Resources.FindObjectsOfTypeAll(type);
            log.LogInfo($"Mod Config UI probe: {typeName} count={objects.Length}.");
            foreach (var instance in objects)
            {
                var label = ReadViewLabel(instance);
                var path = BuildObjectPath(instance);
                var description = string.IsNullOrWhiteSpace(label)
                    ? $"{typeName}: {path}"
                    : $"{typeName}: {path}; View={label}";
                discovered.Add(description);

                foreach (var anchor in AnchorNames.Where(anchor =>
                             string.Equals(anchor, label, StringComparison.OrdinalIgnoreCase) ||
                             path.IndexOf(anchor, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    anchors.Add(anchor);
                }
            }
        }

        foreach (var description in discovered.Take(Math.Max(0, maxLoggedObjects)))
        {
            log.LogInfo("Mod Config UI probe object: " + description);
        }

        if (discovered.Count > maxLoggedObjects)
        {
            log.LogInfo($"Mod Config UI probe omitted {discovered.Count - maxLoggedObjects} objects from the log.");
        }

        var orderedAnchors = AnchorNames.Where(anchors.Contains).ToArray();
        log.LogInfo(
            $"Mod Config UI probe complete. Objects={discovered.Count}; " +
            $"Anchors={(orderedAnchors.Length == 0 ? "none" : string.Join(", ", orderedAnchors))}.");
        return new NavigationProbeResult(discovered.Count, orderedAnchors);
    }

    internal static string ReadViewLabel(object instance)
    {
        var itemField = FindField(instance.GetType(), "item");
        var item = itemField?.GetValue(instance);
        if (item is null)
        {
            return string.Empty;
        }

        var nameMethod = item.GetType().GetMethod(
            "GetName",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        try
        {
            return nameMethod?.Invoke(item, null)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string BuildObjectPath(object instance)
    {
        var transform = ReadMember(instance, "transform");
        if (transform is null)
        {
            return ReadName(instance);
        }

        var segments = new List<string>();
        object? current = transform;
        for (var depth = 0; current is not null && depth < 64; depth++)
        {
            segments.Add(ReadName(current));
            current = ReadMember(current, "parent");
        }

        segments.Reverse();
        return string.Join("/", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string ReadName(object instance)
    {
        return ReadMember(instance, "name")?.ToString() ?? instance.GetType().Name;
    }

    private static object? ReadMember(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        return type.GetProperty(name, flags)?.GetValue(instance, null) ??
               FindField(type, name)?.GetValue(instance);
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, flags);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }
}
