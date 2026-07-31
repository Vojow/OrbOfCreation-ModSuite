using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata;

internal readonly record struct QuickControlNativeGeometry(
    Vector2 AnchorMin,
    Vector2 AnchorMax,
    Vector2 Pivot,
    Vector2 AnchoredPosition,
    Vector2 ControlSize);

internal sealed record QuickControlNativePrimitives(
    RectTransform Anchor,
    NativeButtonStateVisualPrimitives StateVisuals,
    Sprite EmergencyStopIcon,
    QuickControlNativeGeometry Geometry);

/// <summary>
/// Resolves the top-left column from the scene-bound audited
/// <c>UIContentArea.canvas</c> reference, exact native HelpButtons structure, and the exact loaded
/// <c>power-lightning</c> Sprite shipped in <c>sharedassets0.assets</c>.
/// </summary>
internal static class QuickControlNativeAdapter
{
    internal const string AnchorPath = "Canvas/HelpButtons";
    internal const string EmergencyStopIconName = "power-lightning";

    internal static bool TryCapture(
        out QuickControlNativePrimitives? primitives,
        out string reason)
    {
        primitives = null;
        var contentAreaType = Type.GetType("UIContentArea, Assembly-CSharp", false);
        if (contentAreaType is null)
        {
            reason = "audited UIContentArea type is unavailable";
            return false;
        }
        try
        {
            var candidates = Resources.FindObjectsOfTypeAll(contentAreaType)
                .OfType<Component>()
                .OrderBy(NativeObjectPath.Build, StringComparer.Ordinal)
                .ToArray();
            if (!TryResolveAnchor(candidates, out var anchor, out var anchorReason))
            {
                reason = "audited top-left HelpButtons anchor unavailable: " + anchorReason;
                return false;
            }
            if (!TryResolveControlGeometry(anchor!, out var geometry, out var geometryReason))
            {
                reason = "audited top-left button geometry unavailable: " + geometryReason;
                return false;
            }
            if (!NativeViewAdapter.TryCaptureButtonStateVisuals(
                    out var stateVisuals,
                    out var stateReason))
            {
                reason = "quick-control state visual unavailable: " + stateReason;
                return false;
            }
            var sprites = Resources.FindObjectsOfTypeAll(typeof(Sprite))
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
            if (!TryResolveEmergencyStopIcon(sprites, out var emergencyStopIcon, out var iconReason))
            {
                reason = "quick-control emergency-stop icon unavailable: " + iconReason;
                return false;
            }
            primitives = new QuickControlNativePrimitives(
                anchor!,
                stateVisuals!,
                emergencyStopIcon!,
                geometry);
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            reason =
                "quick-control native capture failed while checking UIContentArea.canvas and " +
                "UIViewRadioButton state frames plus the power-lightning Sprite: " +
                $"{root.GetType().FullName}: {root.Message}";
            return false;
        }
    }

    internal static bool TryResolveEmergencyStopIcon(
        IReadOnlyList<Sprite> sprites,
        out Sprite? icon,
        out string reason)
    {
        icon = null;
        var matches = sprites
            .Where(sprite => sprite is not null &&
                             string.Equals(
                                 sprite.name,
                                 EmergencyStopIconName,
                                 StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            reason =
                $"expected exactly one loaded UnityEngine.Sprite named " +
                $"'{EmergencyStopIconName}', found {matches.Length} among {sprites.Count}";
            return false;
        }
        icon = matches[0];
        reason = string.Empty;
        return true;
    }

    internal static bool TryResolveAnchor(
        IReadOnlyList<Component> contentAreas,
        out RectTransform? anchor,
        out string reason)
    {
        anchor = null;
        if (contentAreas.Count == 0)
        {
            reason = "no UIContentArea object exists in the loaded scene";
            return false;
        }
        var matches = new List<RectTransform>();
        var failures = new List<string>();
        foreach (var contentArea in contentAreas)
        {
            var canvasField = FindField(contentArea.GetType(), "canvas");
            if (canvasField is null)
            {
                failures.Add(
                    $"{contentArea.GetType().FullName}.canvas field check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    "actual <missing>");
                continue;
            }
            if (canvasField.FieldType != typeof(RectTransform))
            {
                failures.Add(
                    $"{canvasField.DeclaringType?.FullName}.canvas declared type check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    $"actual {canvasField.FieldType.FullName}");
                continue;
            }
            var canvasValue = canvasField.GetValue(contentArea);
            if (canvasValue is not RectTransform canvas)
            {
                failures.Add(
                    $"{canvasField.DeclaringType?.FullName}.canvas value type check failed at " +
                    $"'{NativeObjectPath.Build(contentArea)}': expected UnityEngine.RectTransform, " +
                    $"actual {canvasValue?.GetType().FullName ?? "<null>"}");
                continue;
            }
            var helpButtons = FindDirectChild(canvas, "HelpButtons");
            if (helpButtons is not RectTransform helpRect ||
                !string.Equals(NativeObjectPath.Build(helpRect), AnchorPath, StringComparison.Ordinal))
            {
                failures.Add(
                    $"'{NativeObjectPath.Build(contentArea)}' canvas has no exact {AnchorPath} child");
                continue;
            }
            if (FindDirectChild(helpRect, "SettingsButton") is null ||
                FindDirectChild(helpRect, "PlayerStatsButton") is null)
            {
                failures.Add(
                    $"{AnchorPath} lacks direct SettingsButton/PlayerStatsButton children");
                continue;
            }
            matches.Add(helpRect);
        }
        if (matches.Count != 1)
        {
            reason = matches.Count == 0
                ? string.Join("; ", failures)
                : $"expected one exact {AnchorPath} anchor, found {matches.Count}";
            return false;
        }
        anchor = matches[0];
        reason = string.Empty;
        return true;
    }

    internal static bool TryResolveControlGeometry(
        RectTransform anchor,
        out QuickControlNativeGeometry geometry,
        out string reason)
    {
        geometry = default;
        if (anchor is null)
        {
            reason = $"{AnchorPath} is unavailable";
            return false;
        }
        if (FindDirectChild(anchor, "SettingsButton") is not RectTransform settings)
        {
            reason = $"{AnchorPath}/SettingsButton type check failed: expected " +
                     "UnityEngine.RectTransform";
            return false;
        }
        if (FindDirectChild(anchor, "PlayerStatsButton") is not RectTransform playerStats)
        {
            reason = $"{AnchorPath}/PlayerStatsButton type check failed: expected " +
                     "UnityEngine.RectTransform";
            return false;
        }

        var settingsSize = new Vector2(settings.rect.width, settings.rect.height);
        var playerSize = new Vector2(playerStats.rect.width, playerStats.rect.height);
        if (settingsSize.x <= 0f || settingsSize.y <= 0f ||
            playerSize.x <= 0f || playerSize.y <= 0f)
        {
            reason = $"{AnchorPath} button rect check failed: expected positive SettingsButton/" +
                     $"PlayerStatsButton sizes, actual {Describe(settingsSize)}/" +
                     Describe(playerSize);
            return false;
        }
        if (!Same(settingsSize, playerSize))
        {
            reason = $"{AnchorPath} button size check failed: expected matching SettingsButton/" +
                     $"PlayerStatsButton sizes, actual {Describe(settingsSize)}/" +
                     Describe(playerSize);
            return false;
        }
        if (!Same(settings.anchorMin, playerStats.anchorMin) ||
            !Same(settings.anchorMax, playerStats.anchorMax) ||
            !Same(settings.pivot, playerStats.pivot))
        {
            reason = $"{AnchorPath} button anchor/pivot check failed: SettingsButton and " +
                     "PlayerStatsButton do not share one layout contract";
            return false;
        }

        var step = new Vector2(
            playerStats.anchoredPosition.x - settings.anchoredPosition.x,
            playerStats.anchoredPosition.y - settings.anchoredPosition.y);
        if (Math.Abs(step.x) > 0.01f || step.y > -playerSize.y)
        {
            reason = $"{AnchorPath} vertical-stack check failed: expected PlayerStatsButton " +
                     $"directly below SettingsButton by at least {playerSize.y:0.###}, actual " +
                     Describe(step);
            return false;
        }

        geometry = new QuickControlNativeGeometry(
            playerStats.anchorMin,
            playerStats.anchorMax,
            playerStats.pivot,
            new Vector2(
                playerStats.anchoredPosition.x + step.x,
                playerStats.anchoredPosition.y + step.y),
            playerSize);
        reason = string.Empty;
        return true;
    }

    private static bool Same(Vector2 left, Vector2 right) =>
        Math.Abs(left.x - right.x) <= 0.01f &&
        Math.Abs(left.y - right.y) <= 0.01f;

    private static string Describe(Vector2 value) => $"({value.x:0.###}, {value.y:0.###})";

    private static Transform? FindDirectChild(Transform parent, string name)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, flags);
            if (field is not null) return field;
        }
        return null;
    }
}
